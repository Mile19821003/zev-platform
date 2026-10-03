using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;
using ZevEntity = global::Zev.Api.Domain.Zev;

namespace Zev.Api.Endpoints;

public static class Phase2AdminEndpoints
{
    public static RouteGroupBuilder MapPhase2AdminEndpoints(this RouteGroupBuilder group)
    {
        MapOrganizations(group);
        MapZevs(group);
        MapBuildings(group);
        MapEntrances(group);
        group.MapAccessEndpoints();
        return group;
    }

    private static void MapOrganizations(RouteGroupBuilder group)
    {
        group.MapGet("/overview", async (ZevDbContext database, CancellationToken cancellationToken) => Results.Ok(new
        {
            organizations = await database.Organizations.CountAsync(cancellationToken),
            activeOrganizations = await database.Organizations.CountAsync(item => item.IsActive, cancellationToken),
            zevs = await database.Zevs.CountAsync(cancellationToken),
            activeZevs = await database.Zevs.CountAsync(item => item.IsActive, cancellationToken)
        }));

        group.MapGet("/organizations", async (int? page, int? pageSize, string? search, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
            var query = database.Organizations.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(item => item.Name.Contains(term) || item.Code.Contains(term));
            }

            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query.OrderBy(item => item.Name).ThenBy(item => item.Id)
                .Skip((currentPage - 1) * currentSize).Take(currentSize).ToListAsync(cancellationToken);
            return Results.Ok(new { page = currentPage, pageSize = currentSize, totalCount, items = items.Select(OrganizationView) });
        });

        group.MapGet("/organizations/{organizationId:guid}", async (Guid organizationId, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            var organization = await database.Organizations.AsNoTracking().SingleOrDefaultAsync(item => item.Id == organizationId, cancellationToken);
            return organization is null ? Results.NotFound() : Results.Ok(OrganizationView(organization));
        });

        group.MapPut("/organizations/{organizationId:guid}", async (Guid organizationId, UpdateOrganizationRequest request, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!AdminValidation.Code(request.Code) || !AdminValidation.Required(request.Name, 200)
                || !AdminValidation.Optional(request.AddressLine, 250)
                || !AdminValidation.Optional(request.CityMunicipality, 120)
                || !AdminValidation.Optional(request.TaxIdentifier, 32)
                || !AdminValidation.Optional(request.ContactName, 200)
                || !AdminValidation.Optional(request.Email, 320)
                || !AdminValidation.Optional(request.Phone, 40)
                || !AdminValidation.TryRowVersion(request.RowVersion, out var rowVersion))
            {
                return Results.BadRequest();
            }

            var organization = await database.Organizations.SingleOrDefaultAsync(item => item.Id == organizationId, cancellationToken);
            if (organization is null) return Results.NotFound();
            var code = request.Code.Trim().ToUpperInvariant();
            if (await database.Organizations.AnyAsync(item => item.Id != organizationId && item.Code == code, cancellationToken))
            {
                return Results.Conflict();
            }

            database.Entry(organization).Property(item => item.RowVersion).OriginalValue = rowVersion;
            organization.Code = code;
            organization.Name = request.Name.Trim();
            organization.AddressLine = AdminValidation.Clean(request.AddressLine);
            organization.CityMunicipality = AdminValidation.Clean(request.CityMunicipality);
            organization.TaxIdentifier = AdminValidation.Clean(request.TaxIdentifier);
            organization.ContactName = AdminValidation.Clean(request.ContactName);
            organization.Email = AdminValidation.Clean(request.Email);
            organization.Phone = AdminValidation.Clean(request.Phone);
            try
            {
                await database.SaveChangesAsync(cancellationToken);
                return Results.Ok(OrganizationView(organization));
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new { error = "Organization was changed by another request." });
            }
        });

        group.MapPatch("/organizations/{organizationId:guid}/status", async (Guid organizationId, UpdateStatusRequest request, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!AdminValidation.TryRowVersion(request.RowVersion, out var rowVersion)) return Results.BadRequest();
            var organization = await database.Organizations.SingleOrDefaultAsync(item => item.Id == organizationId, cancellationToken);
            if (organization is null) return Results.NotFound();
            database.Entry(organization).Property(item => item.RowVersion).OriginalValue = rowVersion;
            organization.IsActive = request.IsActive;
            if (request.IsActive)
            {
                organization.ActivatedOn ??= DateOnly.FromDateTime(DateTime.UtcNow);
                organization.DeactivatedOn = null;
            }
            else
            {
                organization.DeactivatedOn = DateOnly.FromDateTime(DateTime.UtcNow);
            }
            try
            {
                await database.SaveChangesAsync(cancellationToken);
                return Results.Ok(OrganizationView(organization));
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new { error = "Organization was changed by another request." });
            }
        });
    }

    private static void MapZevs(RouteGroupBuilder group)
    {
        group.MapGet("/zevs", async (int? page, int? pageSize, Guid? organizationId, string? search, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
            var query = database.Zevs.AsNoTracking().Include(item => item.Organization).AsQueryable();
            if (organizationId is Guid selectedOrganization) query = query.Where(item => item.OrganizationId == selectedOrganization);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(item => item.Name.Contains(term) || item.Code.Contains(term));
            }

            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query.OrderBy(item => item.Name).ThenBy(item => item.Id)
                .Skip((currentPage - 1) * currentSize).Take(currentSize).ToListAsync(cancellationToken);
            return Results.Ok(new { page = currentPage, pageSize = currentSize, totalCount, items = items.Select(ZevView) });
        });

        group.MapGet("/zevs/{zevId:guid}", async (Guid zevId, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            var zev = await database.Zevs.AsNoTracking().Include(item => item.Organization)
                .SingleOrDefaultAsync(item => item.Id == zevId, cancellationToken);
            return zev is null ? Results.NotFound() : Results.Ok(ZevView(zev));
        });

        group.MapPut("/zevs/{zevId:guid}", async (Guid zevId, UpdateZevRequest request, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!ValidZev(request.Code, request.Name, request.AddressLine, request.CityMunicipality, request.TaxIdentifier,
                request.PrimaryBankAccount, request.ReserveFundBankAccount, request.ContactEmail, request.ContactPhone)
                || !AdminValidation.TryRowVersion(request.RowVersion, out var rowVersion)) return Results.BadRequest();
            var zev = await database.Zevs.Include(item => item.Organization).SingleOrDefaultAsync(item => item.Id == zevId, cancellationToken);
            if (zev is null) return Results.NotFound();
            var code = request.Code.Trim().ToUpperInvariant();
            if (await database.Zevs.AnyAsync(item => item.Id != zevId && item.OrganizationId == zev.OrganizationId && item.Code == code, cancellationToken))
            {
                return Results.Conflict();
            }

            database.Entry(zev).Property(item => item.RowVersion).OriginalValue = rowVersion;
            zev.Code = code;
            zev.Name = request.Name.Trim();
            zev.AddressLine = AdminValidation.Clean(request.AddressLine);
            zev.CityMunicipality = AdminValidation.Clean(request.CityMunicipality);
            zev.TaxIdentifier = AdminValidation.Clean(request.TaxIdentifier);
            zev.PrimaryBankAccount = AdminValidation.Clean(request.PrimaryBankAccount);
            zev.ReserveFundBankAccount = AdminValidation.Clean(request.ReserveFundBankAccount);
            zev.ContactEmail = AdminValidation.Clean(request.ContactEmail);
            zev.ContactPhone = AdminValidation.Clean(request.ContactPhone);
            zev.FoundedOn = request.FoundedOn;
            try
            {
                await database.SaveChangesAsync(cancellationToken);
                return Results.Ok(ZevView(zev));
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new { error = "ZEV was changed by another request." });
            }
        });

        group.MapPatch("/zevs/{zevId:guid}/status", async (Guid zevId, UpdateStatusRequest request, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!AdminValidation.TryRowVersion(request.RowVersion, out var rowVersion)) return Results.BadRequest();
            var zev = await database.Zevs.Include(item => item.Organization).SingleOrDefaultAsync(item => item.Id == zevId, cancellationToken);
            if (zev is null) return Results.NotFound();
            if (request.IsActive && !zev.Organization.IsActive) return Results.Conflict(new { error = "Organization is inactive." });
            database.Entry(zev).Property(item => item.RowVersion).OriginalValue = rowVersion;
            zev.IsActive = request.IsActive;
            try
            {
                await database.SaveChangesAsync(cancellationToken);
                return Results.Ok(ZevView(zev));
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new { error = "ZEV was changed by another request." });
            }
        });
    }

    private static void MapBuildings(RouteGroupBuilder group)
    {
        group.MapGet("/buildings", async (int? page, int? pageSize, Guid? zevId, string? search, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
            var query = database.Buildings.AsNoTracking().Include(item => item.Zev).AsQueryable();
            if (zevId is Guid selectedZev) query = query.Where(item => item.ZevId == selectedZev);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(item => item.Name.Contains(term) || item.Code.Contains(term));
            }
            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query.OrderBy(item => item.Name).ThenBy(item => item.Id)
                .Skip((currentPage - 1) * currentSize).Take(currentSize).ToListAsync(cancellationToken);
            return Results.Ok(new { page = currentPage, pageSize = currentSize, totalCount, items = items.Select(BuildingView) });
        });

        group.MapGet("/buildings/{buildingId:guid}", async (Guid buildingId, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            var building = await database.Buildings.AsNoTracking().Include(item => item.Zev)
                .SingleOrDefaultAsync(item => item.Id == buildingId, cancellationToken);
            return building is null ? Results.NotFound() : Results.Ok(BuildingView(building));
        });

        group.MapPost("/buildings", async (CreateBuildingRequest request, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!ValidBuilding(request.Code, request.Name, request.AddressLine)) return Results.BadRequest();
            var zev = await database.Zevs.AsNoTracking().SingleOrDefaultAsync(item => item.Id == request.ZevId
                && item.IsActive && item.Organization.IsActive, cancellationToken);
            if (zev is null) return Results.NotFound();
            var code = request.Code.Trim().ToUpperInvariant();
            if (await database.Buildings.AnyAsync(item => item.ZevId == zev.Id && item.Code == code, cancellationToken)) return Results.Conflict();
            var building = new Building
            {
                ZevId = zev.Id, OrganizationId = zev.OrganizationId, Code = code, Name = request.Name.Trim(),
                AddressLine = AdminValidation.Clean(request.AddressLine)
            };
            database.Buildings.Add(building);
            await database.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/system/buildings/{building.Id}", new { building.Id, building.ZevId, building.Code, building.Name });
        });

        group.MapPut("/buildings/{buildingId:guid}", async (Guid buildingId, UpdateBuildingRequest request, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!ValidBuilding(request.Code, request.Name, request.AddressLine)
                || !AdminValidation.TryRowVersion(request.RowVersion, out var rowVersion)) return Results.BadRequest();
            var building = await database.Buildings.Include(item => item.Zev).SingleOrDefaultAsync(item => item.Id == buildingId, cancellationToken);
            if (building is null) return Results.NotFound();
            var code = request.Code.Trim().ToUpperInvariant();
            if (await database.Buildings.AnyAsync(item => item.Id != buildingId && item.ZevId == building.ZevId && item.Code == code, cancellationToken)) return Results.Conflict();
            database.Entry(building).Property(item => item.RowVersion).OriginalValue = rowVersion;
            building.Code = code;
            building.Name = request.Name.Trim();
            building.AddressLine = AdminValidation.Clean(request.AddressLine);
            try
            {
                await database.SaveChangesAsync(cancellationToken);
                return Results.Ok(BuildingView(building));
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new { error = "Building was changed by another request." });
            }
        });

        group.MapPatch("/buildings/{buildingId:guid}/status", async (Guid buildingId, UpdateStatusRequest request, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!AdminValidation.TryRowVersion(request.RowVersion, out var rowVersion)) return Results.BadRequest();
            var building = await database.Buildings.Include(item => item.Zev).ThenInclude(item => item.Organization)
                .SingleOrDefaultAsync(item => item.Id == buildingId, cancellationToken);
            if (building is null) return Results.NotFound();
            if (request.IsActive && (!building.Zev.IsActive || !building.Zev.Organization.IsActive)) return Results.Conflict();
            database.Entry(building).Property(item => item.RowVersion).OriginalValue = rowVersion;
            building.IsActive = request.IsActive;
            try
            {
                await database.SaveChangesAsync(cancellationToken);
                return Results.Ok(BuildingView(building));
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new { error = "Building was changed by another request." });
            }
        });
    }

    private static void MapEntrances(RouteGroupBuilder group)
    {
        group.MapGet("/entrances", async (int? page, int? pageSize, Guid? buildingId, Guid? zevId, string? search, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
            var query = database.Entrances.AsNoTracking().Include(item => item.Building).AsQueryable();
            if (buildingId is Guid selectedBuilding) query = query.Where(item => item.BuildingId == selectedBuilding);
            if (zevId is Guid selectedZev) query = query.Where(item => item.ZevId == selectedZev);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(item => item.Name.Contains(term) || item.Code.Contains(term));
            }
            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query.OrderBy(item => item.Building.Name).ThenBy(item => item.Code).ThenBy(item => item.Id)
                .Skip((currentPage - 1) * currentSize).Take(currentSize).ToListAsync(cancellationToken);
            return Results.Ok(new { page = currentPage, pageSize = currentSize, totalCount, items = items.Select(EntranceView) });
        });

        group.MapGet("/entrances/{entranceId:guid}", async (Guid entranceId, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            var entrance = await database.Entrances.AsNoTracking().Include(item => item.Building)
                .SingleOrDefaultAsync(item => item.Id == entranceId, cancellationToken);
            return entrance is null ? Results.NotFound() : Results.Ok(EntranceView(entrance));
        });

        group.MapPost("/entrances", async (CreateEntranceRequest request, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!ValidEntrance(request.Code, request.Name)) return Results.BadRequest();
            var building = await database.Buildings.AsNoTracking().SingleOrDefaultAsync(item => item.Id == request.BuildingId
                && item.IsActive && item.Zev.IsActive && item.Zev.Organization.IsActive, cancellationToken);
            if (building is null) return Results.NotFound();
            var code = request.Code.Trim().ToUpperInvariant();
            if (await database.Entrances.AnyAsync(item => item.BuildingId == building.Id && item.Code == code, cancellationToken)) return Results.Conflict();
            var entrance = new Entrance
            {
                BuildingId = building.Id, ZevId = building.ZevId, OrganizationId = building.OrganizationId,
                Code = code, Name = request.Name.Trim()
            };
            database.Entrances.Add(entrance);
            await database.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/system/entrances/{entrance.Id}", new { entrance.Id, entrance.BuildingId, entrance.Code, entrance.Name });
        });

        group.MapPut("/entrances/{entranceId:guid}", async (Guid entranceId, UpdateEntranceRequest request, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!ValidEntrance(request.Code, request.Name)
                || !AdminValidation.TryRowVersion(request.RowVersion, out var rowVersion)) return Results.BadRequest();
            var entrance = await database.Entrances.Include(item => item.Building).SingleOrDefaultAsync(item => item.Id == entranceId, cancellationToken);
            if (entrance is null) return Results.NotFound();
            var code = request.Code.Trim().ToUpperInvariant();
            if (await database.Entrances.AnyAsync(item => item.Id != entranceId && item.BuildingId == entrance.BuildingId && item.Code == code, cancellationToken)) return Results.Conflict();
            database.Entry(entrance).Property(item => item.RowVersion).OriginalValue = rowVersion;
            entrance.Code = code;
            entrance.Name = request.Name.Trim();
            try
            {
                await database.SaveChangesAsync(cancellationToken);
                return Results.Ok(EntranceView(entrance));
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new { error = "Entrance was changed by another request." });
            }
        });

        group.MapPatch("/entrances/{entranceId:guid}/status", async (Guid entranceId, UpdateStatusRequest request, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!AdminValidation.TryRowVersion(request.RowVersion, out var rowVersion)) return Results.BadRequest();
            var entrance = await database.Entrances.Include(item => item.Building).ThenInclude(item => item.Zev)
                .SingleOrDefaultAsync(item => item.Id == entranceId, cancellationToken);
            if (entrance is null) return Results.NotFound();
            if (request.IsActive && (!entrance.Building.IsActive || !entrance.Building.Zev.IsActive)) return Results.Conflict();
            database.Entry(entrance).Property(item => item.RowVersion).OriginalValue = rowVersion;
            entrance.IsActive = request.IsActive;
            try
            {
                await database.SaveChangesAsync(cancellationToken);
                return Results.Ok(EntranceView(entrance));
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new { error = "Entrance was changed by another request." });
            }
        });
    }

    private static bool ValidZev(string? code, string? name, string? address, string? city, string? taxIdentifier,
        string? primaryAccount, string? reserveAccount, string? contactEmail, string? contactPhone) => AdminValidation.Code(code)
        && AdminValidation.Required(name, 200) && AdminValidation.Optional(address, 250)
        && AdminValidation.Optional(city, 120) && AdminValidation.Optional(taxIdentifier, 32)
        && AdminValidation.Optional(primaryAccount, 64) && AdminValidation.Optional(reserveAccount, 64)
        && AdminValidation.Optional(contactEmail, 320) && AdminValidation.Optional(contactPhone, 40);

    private static bool ValidBuilding(string? code, string? name, string? address) =>
        AdminValidation.Code(code) && AdminValidation.Required(name, 200) && AdminValidation.Optional(address, 250);

    private static bool ValidEntrance(string? code, string? name) =>
        AdminValidation.Code(code) && AdminValidation.Required(name, 200);

    private static object OrganizationView(Organization item) => new
    {
        item.Id, item.Code, item.Name, item.AddressLine, item.CityMunicipality, item.TaxIdentifier,
        item.ContactName, item.Email, item.Phone, item.ActivatedOn, item.DeactivatedOn,
        item.IsActive, rowVersion = Convert.ToBase64String(item.RowVersion)
    };

    private static object ZevView(ZevEntity item) => new
    {
        item.Id, item.OrganizationId, organizationName = item.Organization.Name, item.Code, item.Name,
        item.AddressLine, item.CityMunicipality, item.TaxIdentifier, item.PrimaryBankAccount,
        item.ReserveFundBankAccount, item.ContactEmail, item.ContactPhone, item.FoundedOn,
        item.IsActive, rowVersion = Convert.ToBase64String(item.RowVersion)
    };

    private static object BuildingView(Building item) => new
    {
        item.Id, item.OrganizationId, item.ZevId, zevName = item.Zev.Name, item.Code, item.Name,
        item.AddressLine, item.IsActive, rowVersion = Convert.ToBase64String(item.RowVersion)
    };

    private static object EntranceView(Entrance item) => new
    {
        item.Id, item.OrganizationId, item.ZevId, item.BuildingId, buildingName = item.Building.Name,
        item.Code, item.Name, item.IsActive, rowVersion = Convert.ToBase64String(item.RowVersion)
    };
}

public sealed record UpdateStatusRequest(bool IsActive, string RowVersion);
public sealed record UpdateOrganizationRequest(string Code, string Name, string RowVersion,
    string? AddressLine = null, string? CityMunicipality = null, string? TaxIdentifier = null,
    string? ContactName = null, string? Email = null, string? Phone = null);
public sealed record UpdateZevRequest(string Code, string Name, string? AddressLine, string? CityMunicipality,
    string? TaxIdentifier, string? PrimaryBankAccount, string? ReserveFundBankAccount, string RowVersion,
    string? ContactEmail = null, string? ContactPhone = null, DateOnly? FoundedOn = null);
public sealed record CreateBuildingRequest(Guid ZevId, string Code, string Name, string? AddressLine);
public sealed record UpdateBuildingRequest(string Code, string Name, string? AddressLine, string RowVersion);
public sealed record CreateEntranceRequest(Guid BuildingId, string Code, string Name);
public sealed record UpdateEntranceRequest(string Code, string Name, string RowVersion);
