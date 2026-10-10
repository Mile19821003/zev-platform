using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Endpoints;
using Zev.Api.Security;
using Zev.Api.Registry;
using Zev.Api.Finance;
using Zev.Api.Payments;
using Zev.Api.Storage;
using Zev.Api.Maintenance;
using Zev.Api.Hodograms;
using Zev.Api.Community;
using ZevEntity = global::Zev.Api.Domain.Zev;

var seedUatFinance = args.Contains("--seed-uat-finance", StringComparer.Ordinal);
var builder = WebApplication.CreateBuilder(args.Where(argument => argument != "--seed-uat-finance").ToArray());
builder.Services.AddHttpContextAccessor();
builder.Services.AddProblemDetails();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var proxy in builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [])
    {
        options.KnownProxies.Add(IPAddress.Parse(proxy));
    }
});
builder.Services.AddDbContext<ZevDbContext>(options => options.UseSqlServer(
    builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("ConnectionStrings:Default is required.")));
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddSingleton<PasswordPolicy>();
builder.Services.AddScoped<PasswordManagementService>();
builder.Services.AddScoped<TenantAccess>();
builder.Services.AddScoped<EffectiveAccessService>();
builder.Services.AddScoped<OwnershipService>();
builder.Services.AddScoped<MonthlyBillingEngine>();
builder.Services.AddSingleton<IInvoiceNumberProvider, InvoiceNumberProvider>();
builder.Services.AddScoped<InvoiceLifecycleService>();
builder.Services.AddScoped<UatFinanceSeed>();
builder.Services.AddScoped<PaymentReconciliationEngine>();
builder.Services.AddScoped<PaymentWorkflowService>();
builder.Services.AddScoped<BankAccountBalanceService>();
builder.Services.AddScoped<Zev.Api.Lookups.LookupService>();
builder.Services.AddScoped<OwnerCardService>();
builder.Services.AddScoped<PaymentReferenceService>();
builder.Services.AddSingleton<IPaymentQrProvider, UnconfiguredPaymentQrProvider>();
builder.Services.AddSingleton<IPaymentSlipProvider, GenericPdfPaymentSlipProvider>();
builder.Services.AddScoped<ExpensePostingService>();
builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();
builder.Services.AddScoped<MaintenanceScheduleService>();
builder.Services.AddScoped<HodogramService>();
builder.Services.AddScoped<VotingService>();
builder.Services.AddSingleton<IBankStatementParser>(new DelimitedStatementParser("CSV", ','));
builder.Services.AddSingleton<IBankStatementParser>(new DelimitedStatementParser("TXT", ';'));
builder.Services.AddSingleton<IBankStatementParser, XmlStatementParser>();
builder.Services.AddSingleton<IBankStatementParser, Camt053StatementParser>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddScoped<IAuthorizationHandler, SystemAdminAuthorizationHandler>();
builder.Services.AddAuthorization(options => options.AddPolicy("SystemAdministrator", policy =>
    policy.RequireAuthenticatedUser().AddRequirements(new SystemAdminRequirement())));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    var tokenService = new TokenService(builder.Configuration);
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = tokenService.SigningKey,
        ValidateIssuer = true,
        ValidIssuer = tokenService.Issuer,
        ValidateAudience = true,
        ValidAudience = tokenService.Audience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30)
    };
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            if (!Guid.TryParse(context.Principal?.FindFirstValue("sub"), out var userId)
                || !int.TryParse(context.Principal?.FindFirstValue("security_version"), out var version))
            {
                context.Fail("Invalid session.");
                return;
            }
            var database = context.HttpContext.RequestServices.GetRequiredService<ZevDbContext>();
            var user = await database.Users.AsNoTracking().Where(item => item.Id == userId && item.IsActive)
                .Select(item => new { item.SecurityVersion, item.MustChangePassword })
                .SingleOrDefaultAsync(context.HttpContext.RequestAborted);
            if (user is null || user.SecurityVersion != version)
            {
                context.Fail("Session revoked.");
                return;
            }
            var path = context.HttpContext.Request.Path.Value;
            if (user.MustChangePassword && path is not ("/api/me" or "/api/me/password"
                or "/api/me/password/mobile"))
                context.Fail("Password change required.");
        }
    };
});
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
    options.AddPolicy("upload", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirstValue("sub") ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});
builder.Services.AddCors(options => options.AddPolicy("web", policy => policy
    .WithOrigins(builder.Configuration["Web:Origin"] ?? "http://localhost:5173")
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
builder.Services.AddOpenApi();

var app = builder.Build();
if (seedUatFinance)
{
    var enabled = app.Configuration.GetValue<bool>("Seed:UatFinanceEnabled");
    if (!enabled || !app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("UAT"))
    {
        Console.Error.WriteLine("FAIL UAT finance seed requires Development/UAT and Seed:UatFinanceEnabled=true.");
        Environment.ExitCode = 1;
        return;
    }

    using var seedScope = app.Services.CreateScope();
    await seedScope.ServiceProvider.GetRequiredService<ZevDbContext>().Database.MigrateAsync();
    var seedResult = await seedScope.ServiceProvider.GetRequiredService<UatFinanceSeed>().RunAsync(
        enabled, app.Environment.EnvironmentName, app.Configuration["UAT_FINANCE_MANAGER_EMAIL"],
        app.Configuration["UAT_FINANCE_MANAGER_PASSWORD"], CancellationToken.None);
    foreach (var check in seedResult.Checks)
        Console.WriteLine($"{(check.Value ? "PASS" : "FAIL")} {check.Key}");
    Console.WriteLine("Expected UAT-A01 = 53.00");
    Console.WriteLine("Expected UAT-A02 = 69.00");
    Console.WriteLine("Expected total = 122.00");
    Console.WriteLine($"Actual values = UAT-A01={seedResult.UnitUatA01?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) ?? "N/A"}; " +
        $"UAT-A02={seedResult.UnitUatA02?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) ?? "N/A"}; " +
        $"total={seedResult.Total?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) ?? "N/A"}");
    if (seedResult.Error is not null) Console.Error.WriteLine(seedResult.Error);
    Environment.ExitCode = seedResult.Success ? 0 : 1;
    return;
}
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<ZevDbContext>().Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseCors("web");
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapOpenApi();
app.MapGet("/health/live", () => Results.Ok(new { status = "healthy" }));
app.MapGet("/health/ready", async (ZevDbContext database, CancellationToken cancellationToken) =>
    await database.Database.CanConnectAsync(cancellationToken)
        ? Results.Ok(new { status = "ready", database = "up" })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));

var auth = app.MapGroup("/api/auth");
auth.MapPost("/login", async (LoginRequest request, AuthService service, HttpContext context, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["credentials"] = ["Email and password are required."] });
    }

    var result = await service.LoginAsync(request.Email, request.Password, cancellationToken);
    if (result is null)
    {
        return Results.Unauthorized();
    }

    SetRefreshCookie(context, result.RefreshToken);
    return Results.Ok(new AccessTokenResponse(result.AccessToken, result.AccessExpiresUtc));
}).RequireRateLimiting("auth");

auth.MapPost("/refresh", async (AuthService service, HttpContext context, CancellationToken cancellationToken) =>
{
    var refreshToken = context.Request.Cookies[TokenService.RefreshCookieName];
    if (string.IsNullOrEmpty(refreshToken))
    {
        return Results.Unauthorized();
    }

    var result = await service.RefreshAsync(refreshToken, cancellationToken);
    if (result is null)
    {
        context.Response.Cookies.Delete(TokenService.RefreshCookieName, RefreshCookieOptions(context));
        return Results.Unauthorized();
    }

    SetRefreshCookie(context, result.RefreshToken);
    return Results.Ok(new AccessTokenResponse(result.AccessToken, result.AccessExpiresUtc));
}).RequireRateLimiting("auth");

auth.MapPost("/logout", async (AuthService service, HttpContext context, CancellationToken cancellationToken) =>
{
    await service.RevokeAsync(context.Request.Cookies[TokenService.RefreshCookieName], cancellationToken);
    context.Response.Cookies.Delete(TokenService.RefreshCookieName, RefreshCookieOptions(context));
    return Results.NoContent();
});

auth.MapPost("/mobile/login", async (LoginRequest request, AuthService service, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        return Results.BadRequest();
    var result = await service.LoginAsync(request.Email, request.Password, cancellationToken);
    return result is null ? Results.Unauthorized()
        : Results.Ok(new MobileTokenResponse(result.AccessToken, result.AccessExpiresUtc, result.RefreshToken));
}).RequireRateLimiting("auth");

auth.MapPost("/mobile/refresh", async (MobileRefreshRequest request, AuthService service,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.RefreshToken)) return Results.Unauthorized();
    var result = await service.RefreshAsync(request.RefreshToken, cancellationToken);
    return result is null ? Results.Unauthorized()
        : Results.Ok(new MobileTokenResponse(result.AccessToken, result.AccessExpiresUtc, result.RefreshToken));
}).RequireRateLimiting("auth");

auth.MapPost("/mobile/logout", async (MobileRefreshRequest request, AuthService service,
    CancellationToken cancellationToken) =>
{
    await service.RevokeAsync(request.RefreshToken, cancellationToken);
    return Results.NoContent();
}).RequireRateLimiting("auth");

var api = app.MapGroup("/api").RequireAuthorization();
api.MapFinanceEndpoints();
api.MapInvoiceEndpoints();
api.MapInvoiceLifecycleEndpoints();
api.MapPaymentEndpoints();
api.MapPaymentSlipEndpoints();
api.MapStoredFileEndpoints();
api.MapExpenseEndpoints();
api.MapLegalEndpoints();
api.MapMaintenanceEndpoints();
api.MapHodogramEndpoints();
api.MapCommunityEndpoints();
api.MapVotingEndpoints();
api.MapReportingEndpoints();
api.MapManagerRegistryEndpoints();
api.MapAccessOverrideEndpoints();
api.MapSystemSettingsEndpoints();
api.MapLookupEndpoints();
api.MapGet("/me", async (TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
{
    if (access.CurrentUserId is not Guid userId)
    {
        return Results.Unauthorized();
    }

    var user = await database.Users.AsNoTracking().Where(item => item.Id == userId && item.IsActive)
        .Select(item => new { item.Id, item.Email, item.IsSystemAdministrator,
            item.MustChangePassword }).SingleOrDefaultAsync(cancellationToken);
    if (user is null)
    {
        return Results.Unauthorized();
    }

    if (user.MustChangePassword)
        return Results.Ok(new { user.Id, user.Email, user.IsSystemAdministrator,
            user.MustChangePassword, memberships = Array.Empty<object>() });

    var memberships = await database.Memberships.AsNoTracking().Where(item => item.UserId == userId && item.IsActive)
        .Select(item => new { item.OrganizationId, item.ZevId, item.Role }).ToListAsync(cancellationToken);
    return Results.Ok(new { user.Id, user.Email, user.IsSystemAdministrator, user.MustChangePassword,
        memberships = memberships.Select(item => new { item.OrganizationId, item.ZevId, role = item.Role.ToString() }) });
});

api.MapPost("/me/password", async (ChangePasswordRequest request, TenantAccess access,
    PasswordManagementService passwords, HttpContext context,
    CancellationToken cancellationToken) =>
{
    if (access.CurrentUserId is not Guid userId) return Results.Unauthorized();
    PasswordChangeResult result;
    try
    {
        result = await passwords.ChangeAsync(userId, request.CurrentPassword,
            request.NewPassword, request.ConfirmPassword, context.TraceIdentifier, cancellationToken);
    }
    catch (DbUpdateConcurrencyException)
    {
        return Results.Conflict(new { error = "Nalog je izmijenjen drugim zahtjevom. Prijavite se ponovo." });
    }
    if (result.Tokens is null) return Results.BadRequest(new { error = result.Error });
    SetRefreshCookie(context, result.Tokens.RefreshToken);
    return Results.Ok(new AccessTokenResponse(result.Tokens.AccessToken,
        result.Tokens.AccessExpiresUtc));
}).RequireRateLimiting("auth");

api.MapPost("/me/password/mobile", async (ChangePasswordRequest request, TenantAccess access,
    PasswordManagementService passwords, HttpContext context,
    CancellationToken cancellationToken) =>
{
    if (access.CurrentUserId is not Guid userId) return Results.Unauthorized();
    PasswordChangeResult result;
    try
    {
        result = await passwords.ChangeAsync(userId, request.CurrentPassword,
            request.NewPassword, request.ConfirmPassword, context.TraceIdentifier, cancellationToken);
    }
    catch (DbUpdateConcurrencyException)
    {
        return Results.Conflict(new { error = "Nalog je izmijenjen drugim zahtjevom. Prijavite se ponovo." });
    }
    if (result.Tokens is null) return Results.BadRequest(new { error = result.Error });
    return Results.Ok(new MobileTokenResponse(result.Tokens.AccessToken,
        result.Tokens.AccessExpiresUtc, result.Tokens.RefreshToken));
}).RequireRateLimiting("auth");

api.MapGet("/me/zevs", async (int? page, int? pageSize, TenantAccess access, EffectiveAccessService accessService,
    ZevDbContext database, IConfiguration configuration, CancellationToken cancellationToken) =>
{
    if (access.CurrentUserId is not Guid userId || !await database.Users.AnyAsync(item => item.Id == userId && item.IsActive, cancellationToken))
    {
        return Results.Unauthorized();
    }

    var currentPage = Math.Max(page ?? 1, 1);
    var currentPageSize = Math.Clamp(pageSize ?? 20, 1, 100);
    var isAdministrator = await access.IsSystemAdministratorAsync(userId, cancellationToken);
    var includeHistory = configuration.GetValue<bool>("Privacy:HistoricalSelfAccess");
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var candidates = await database.Zevs.AsNoTracking().Where(zev => zev.IsActive && zev.Organization.IsActive
        && (isAdministrator || database.Memberships.Any(membership => membership.UserId == userId
                && membership.IsActive && membership.OrganizationId == zev.OrganizationId
                && (membership.ZevId == zev.Id || (membership.ZevId == null
                    && (membership.Role == MembershipRole.OrganizationAdministrator || membership.Role == MembershipRole.Accountant))))
            || database.Ownerships.Any(ownership => ownership.ZevId == zev.Id
                && ownership.ValidFrom <= today && (ownership.ValidTo == null || ownership.ValidTo > today)
                && ownership.PropertyUnit.IsActive && ownership.Person.IsActive && ownership.Person.UserId == userId
                && ownership.Person.UserLinkStatus == UserPersonLinkStatus.Verified)
            || (includeHistory && database.Ownerships.Any(ownership => ownership.ZevId == zev.Id
                && ownership.ValidTo != null && ownership.ValidTo <= today
                && ownership.Person.IsActive && ownership.Person.UserId == userId
                && ownership.Person.UserLinkStatus == UserPersonLinkStatus.Verified))
            || database.AccessOverrides.Any(item => item.ZevId == zev.Id && item.UserId == userId
                && item.PermissionKey == AccessPermissions.Read && item.Effect == AccessEffect.Grant
                && item.RevokedUtc == null && item.ValidFromUtc <= DateTimeOffset.UtcNow
                && (item.ValidToUtc == null || item.ValidToUtc > DateTimeOffset.UtcNow))))
        .OrderBy(zev => zev.Name).ThenBy(zev => zev.Id)
        .Select(zev => new { zev.Id, zev.OrganizationId, OrganizationName = zev.Organization.Name, zev.Code, zev.Name, zev.AddressLine })
        .ToListAsync(cancellationToken);
    var visible = new List<object>();
    foreach (var zev in candidates)
    {
        var rights = await accessService.GetEffectiveZevAccess(userId, zev.Id, cancellationToken);
        var historicalSelfAccess = !rights.CanRead
            && await accessService.HasHistoricalZevAccessAsync(userId, zev.Id, cancellationToken);
        if (rights.CanRead || historicalSelfAccess)
        {
            var permissionContext = await accessService.GetPermissionContextAsync(userId, zev.Id, cancellationToken);
            visible.Add(new { zev.Id, zev.OrganizationId, zev.OrganizationName, zev.Code, zev.Name,
                AddressLine = rights.CanRead ? zev.AddressLine : null, rights.Sources, rights.IsOwner, rights.CanManageFinances,
                rights.CanManageLegalCases, rights.CanManageMaintenance, historicalSelfAccess,
                permissionContext.IsStaff,
                permissions = permissionContext.Permissions.Where(item => item.Effective).Select(item => item.Key).ToArray() });
        }
    }
    return Results.Ok(new { page = currentPage, pageSize = currentPageSize, totalCount = visible.Count,
        items = visible.Skip((currentPage - 1) * currentPageSize).Take(currentPageSize) });
});

api.MapGet("/me/property-units", async (TenantAccess access, ZevDbContext database,
    CancellationToken cancellationToken) =>
{
    if (access.CurrentUserId is not Guid userId || !await database.Users.AnyAsync(item => item.Id == userId
        && item.IsActive, cancellationToken)) return Results.Unauthorized();
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var units = await database.PropertyUnits.AsNoTracking().Where(unit => unit.IsActive
        && (database.Ownerships.Any(ownership => ownership.PropertyUnitId == unit.Id
            && ownership.Person.UserId == userId && ownership.Person.IsActive
            && ownership.Person.UserLinkStatus == UserPersonLinkStatus.Verified && ownership.ValidFrom <= today
            && (ownership.ValidTo == null || ownership.ValidTo > today))
            || database.Occupancies.Any(occupancy => occupancy.PropertyUnitId == unit.Id
                && occupancy.Person.UserId == userId && occupancy.Person.IsActive
                && occupancy.Person.UserLinkStatus == UserPersonLinkStatus.Verified && occupancy.ValidFrom <= today
                && (occupancy.ValidTo == null || occupancy.ValidTo > today))))
        .OrderBy(unit => unit.Code).Take(100)
        .Select(unit => new { unit.Id, unit.ZevId, unit.BuildingId, unit.EntranceId, unit.Code,
            unit.AreaSquareMeters, type = unit.Type.ToString() }).ToListAsync(cancellationToken);
    var readableZevs = new HashSet<Guid>();
    foreach (var zevId in units.Select(item => item.ZevId).Distinct())
        if (await access.CanReadZevAsync(zevId, cancellationToken)) readableZevs.Add(zevId);
    return Results.Ok(units.Where(item => readableZevs.Contains(item.ZevId)));
});

api.MapGet("/organizations/{organizationId:guid}", async (Guid organizationId, TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
{
    if (!await access.CanReadOrganizationAsync(organizationId, cancellationToken))
    {
        return Results.NotFound();
    }

    return Results.Ok(await database.Organizations.AsNoTracking().Where(item => item.Id == organizationId)
        .Select(item => new { item.Id, item.Code, item.Name }).SingleAsync(cancellationToken));
});

api.MapGet("/zevs/{zevId:guid}", async (Guid zevId, TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
{
    if (!await access.CanReadZevAsync(zevId, cancellationToken))
    {
        return Results.NotFound();
    }

    return Results.Ok(await database.Zevs.AsNoTracking().Where(item => item.Id == zevId)
        .Select(item => new { item.Id, item.OrganizationId, item.Code, item.Name }).SingleAsync(cancellationToken));
});

api.MapGet("/zevs/{zevId:guid}/buildings", async (Guid zevId, TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
{
    if (!await access.CanReadZevAsync(zevId, cancellationToken))
    {
        return Results.NotFound();
    }

    var buildings = await database.Buildings.AsNoTracking().Where(item => item.ZevId == zevId && item.IsActive)
        .OrderBy(item => item.Name).Select(item => new { item.Id, item.Code, item.Name, item.AddressLine })
        .Take(100).ToListAsync(cancellationToken);
    return Results.Ok(buildings);
});

api.MapGet("/zevs/{zevId:guid}/entrances", async (Guid zevId, TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
{
    if (!await access.CanReadZevAsync(zevId, cancellationToken))
    {
        return Results.NotFound();
    }

    var entrances = await database.Entrances.AsNoTracking()
        .Where(item => item.ZevId == zevId && item.IsActive && item.Building.IsActive)
        .OrderBy(item => item.Building.Name).ThenBy(item => item.Code)
        .Select(item => new { item.Id, item.BuildingId, item.Code, item.Name }).Take(100).ToListAsync(cancellationToken);
    return Results.Ok(entrances);
});

var system = api.MapGroup("/system").RequireAuthorization("SystemAdministrator");
system.MapGet("/users/{userId:guid}", async (Guid userId, ZevDbContext database, CancellationToken cancellationToken) =>
{
    var user = await database.Users.AsNoTracking().Where(item => item.Id == userId)
        .Select(item => new { item.Id, item.Email, item.IsActive,
            item.IsSystemAdministrator, item.MustChangePassword })
        .SingleOrDefaultAsync(cancellationToken);
    return user is null ? Results.NotFound() : Results.Ok(user);
});

system.MapGet("/memberships/{membershipId:guid}", async (Guid membershipId, ZevDbContext database, CancellationToken cancellationToken) =>
{
    var membership = await database.Memberships.AsNoTracking().Where(item => item.Id == membershipId)
        .Select(item => new { item.Id, item.UserId, item.OrganizationId, item.ZevId, item.Role, item.IsActive })
        .SingleOrDefaultAsync(cancellationToken);
    return membership is null ? Results.NotFound() : Results.Ok(membership);
});

system.MapPost("/organizations", async (CreateOrganizationRequest request, ZevDbContext database, CancellationToken cancellationToken) =>
{
    if (!ValidCode(request.Code) || string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200
        || !ValidOptional(request.AddressLine, 250) || !ValidOptional(request.CityMunicipality, 120)
        || !ValidOptional(request.TaxIdentifier, 32) || !ValidOptional(request.ContactName, 200)
        || !ValidOptional(request.Email, 320) || !ValidOptional(request.Phone, 40))
    {
        return Results.BadRequest();
    }

    var organization = new Organization
    {
        Code = request.Code.Trim().ToUpperInvariant(), Name = request.Name.Trim(),
        AddressLine = NormalizeOptional(request.AddressLine), CityMunicipality = NormalizeOptional(request.CityMunicipality),
        TaxIdentifier = NormalizeOptional(request.TaxIdentifier), ContactName = NormalizeOptional(request.ContactName),
        Email = NormalizeOptional(request.Email), Phone = NormalizeOptional(request.Phone),
        ActivatedOn = DateOnly.FromDateTime(DateTime.UtcNow)
    };
    if (await database.Organizations.AnyAsync(item => item.Code == organization.Code, cancellationToken))
    {
        return Results.Conflict();
    }

    database.Organizations.Add(organization);
    await database.SaveChangesAsync(cancellationToken);
    return Results.Created($"/api/organizations/{organization.Id}", new { organization.Id, organization.Code, organization.Name });
});

system.MapPost("/zevs", async (CreateZevRequest request, ZevDbContext database, CancellationToken cancellationToken) =>
{
    if (!ValidCode(request.Code) || string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200
        || !ValidOptional(request.AddressLine, 250) || !ValidOptional(request.CityMunicipality, 120)
        || !ValidOptional(request.TaxIdentifier, 32) || !ValidOptional(request.PrimaryBankAccount, 64)
        || !ValidOptional(request.ReserveFundBankAccount, 64) || !ValidOptional(request.ContactEmail, 320)
        || !ValidOptional(request.ContactPhone, 40))
    {
        return Results.BadRequest();
    }

    if (!await database.Organizations.AnyAsync(item => item.Id == request.OrganizationId && item.IsActive, cancellationToken))
    {
        return Results.NotFound();
    }

    var zev = new ZevEntity
    {
        OrganizationId = request.OrganizationId,
        Code = request.Code.Trim().ToUpperInvariant(),
        Name = request.Name.Trim(),
        AddressLine = NormalizeOptional(request.AddressLine),
        CityMunicipality = NormalizeOptional(request.CityMunicipality),
        TaxIdentifier = NormalizeOptional(request.TaxIdentifier),
        PrimaryBankAccount = NormalizeOptional(request.PrimaryBankAccount),
        ReserveFundBankAccount = NormalizeOptional(request.ReserveFundBankAccount),
        ContactEmail = NormalizeOptional(request.ContactEmail), ContactPhone = NormalizeOptional(request.ContactPhone),
        FoundedOn = request.FoundedOn
    };
    if (await database.Zevs.AnyAsync(item => item.OrganizationId == zev.OrganizationId && item.Code == zev.Code, cancellationToken))
    {
        return Results.Conflict();
    }

    database.Zevs.Add(zev);
    await database.SaveChangesAsync(cancellationToken);
    return Results.Created($"/api/zevs/{zev.Id}", new { zev.Id, zev.OrganizationId, zev.Code, zev.Name });
});

system.MapPost("/users", async (CreateUserRequest request, ZevDbContext database,
    IPasswordHasher<AppUser> hasher, PasswordPolicy policy, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Email) || !new EmailAddressAttribute().IsValid(request.Email)
        || request.Email.Length > 320)
    {
        return Results.BadRequest(new { error = "Unesite ispravnu email adresu." });
    }
    if (policy.Validate(request.Password) is string failure)
        return Results.BadRequest(new { error = failure });

    var user = new AppUser { Email = request.Email.Trim(), NormalizedEmail = request.Email.Trim().ToUpperInvariant() };
    if (await database.Users.AnyAsync(item => item.NormalizedEmail == user.NormalizedEmail, cancellationToken))
    {
        return Results.Conflict();
    }

    user.PasswordHash = hasher.HashPassword(user, request.Password);
    database.Users.Add(user);
    await database.SaveChangesAsync(cancellationToken);
    return Results.Created($"/api/system/users/{user.Id}", new { user.Id, user.Email });
});

system.MapPost("/users/{userId:guid}/reset-password", async (Guid userId,
    ResetUserPasswordRequest request, TenantAccess access, PasswordPolicy policy,
    PasswordManagementService passwords, HttpContext context, CancellationToken cancellationToken) =>
{
    if (access.CurrentUserId is not Guid actorId) return Results.Unauthorized();
    if (request.NewPassword != request.ConfirmPassword)
        return Results.BadRequest(new { error = "Lozinke se ne podudaraju." });
    if (policy.Validate(request.NewPassword) is string failure)
        return Results.BadRequest(new { error = failure });
    PasswordResetResult result;
    try
    {
        result = await passwords.ResetAsync(actorId, userId, request.NewPassword,
            request.RequireChange, context.TraceIdentifier, cancellationToken);
    }
    catch (DbUpdateConcurrencyException)
    {
        return Results.Conflict(new { error = "Nalog je izmijenjen drugim zahtjevom. Pokušajte ponovo." });
    }
    return result switch
    {
        PasswordResetResult.Success => Results.NoContent(),
        PasswordResetResult.Unchanged => Results.BadRequest(new
            { error = "Nova lozinka mora biti različita od prethodne." }),
        _ => Results.NotFound()
    };
}).RequireRateLimiting("auth");

system.MapPost("/memberships", async (CreateMembershipRequest request, ZevDbContext database, CancellationToken cancellationToken) =>
{
    if (!Enum.TryParse<MembershipRole>(request.Role, true, out var role) || !Enum.IsDefined(role)
        || role == MembershipRole.Owner
        || (request.ZevId is null && role is not (MembershipRole.OrganizationAdministrator or MembershipRole.Accountant))
        || (request.ZevId is not null && role == MembershipRole.OrganizationAdministrator))
    {
        return Results.BadRequest();
    }

    if (!await database.Users.AnyAsync(item => item.Id == request.UserId && item.IsActive, cancellationToken)
        || !await database.Organizations.AnyAsync(item => item.Id == request.OrganizationId && item.IsActive, cancellationToken)
        || (request.ZevId is Guid zevId && !await database.Zevs.AnyAsync(item => item.Id == zevId
            && item.OrganizationId == request.OrganizationId && item.IsActive, cancellationToken)))
    {
        return Results.NotFound();
    }

    if (await database.Memberships.AnyAsync(item => item.UserId == request.UserId
        && item.OrganizationId == request.OrganizationId && item.ZevId == request.ZevId && item.Role == role, cancellationToken))
    {
        return Results.Conflict();
    }

    var membership = new Membership { UserId = request.UserId, OrganizationId = request.OrganizationId, ZevId = request.ZevId, Role = role };
    database.Memberships.Add(membership);
    await database.SaveChangesAsync(cancellationToken);
    return Results.Created($"/api/system/memberships/{membership.Id}", new { membership.Id, membership.UserId, membership.OrganizationId, membership.ZevId, membership.Role });
});

system.MapPhase2AdminEndpoints();
system.MapRegistryAdminEndpoints();

await AdminBootstrap.RunAsync(app.Services, app.Configuration, app.Lifetime.ApplicationStopping);
await app.RunAsync();

static bool ValidCode(string? code) => !string.IsNullOrWhiteSpace(code) && code.Length <= 50
    && code.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

static bool ValidOptional(string? value, int maxLength) => value is null || value.Trim().Length <= maxLength;

static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

static CookieOptions RefreshCookieOptions(HttpContext context) => new()
{
    HttpOnly = true,
    Secure = !context.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment(),
    SameSite = SameSiteMode.Strict,
    Path = "/api/auth",
    MaxAge = TimeSpan.FromDays(30)
};

static void SetRefreshCookie(HttpContext context, string token) =>
    context.Response.Cookies.Append(TokenService.RefreshCookieName, token, RefreshCookieOptions(context));

public sealed record LoginRequest(string Email, string Password);
public sealed record AccessTokenResponse(string AccessToken, DateTimeOffset ExpiresUtc);
public sealed record MobileTokenResponse(string AccessToken, DateTimeOffset ExpiresUtc, string RefreshToken);
public sealed record MobileRefreshRequest(string? RefreshToken);
public sealed record CreateOrganizationRequest(string Code, string Name, string? AddressLine = null,
    string? CityMunicipality = null, string? TaxIdentifier = null, string? ContactName = null,
    string? Email = null, string? Phone = null);
public sealed record CreateZevRequest(Guid OrganizationId, string Code, string Name,
    string? AddressLine = null, string? CityMunicipality = null, string? TaxIdentifier = null,
    string? PrimaryBankAccount = null, string? ReserveFundBankAccount = null,
    string? ContactEmail = null, string? ContactPhone = null, DateOnly? FoundedOn = null);
public sealed record CreateUserRequest(string Email, string Password);
public sealed record ChangePasswordRequest(string? CurrentPassword, string NewPassword, string ConfirmPassword);
public sealed record ResetUserPasswordRequest(string NewPassword, string ConfirmPassword, bool RequireChange);
public sealed record CreateMembershipRequest(Guid UserId, Guid OrganizationId, Guid? ZevId, string Role);

public partial class Program { }
