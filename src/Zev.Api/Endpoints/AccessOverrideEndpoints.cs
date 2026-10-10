using Microsoft.EntityFrameworkCore;
using System.Data;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Security;

namespace Zev.Api.Endpoints;

public static class AccessOverrideEndpoints
{
    public static RouteGroupBuilder MapAccessOverrideEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/zevs/{zevId:guid}/access/permissions", async (Guid zevId, TenantAccess access,
            EffectiveAccessService effectiveAccess, CancellationToken cancellationToken) =>
        {
            if (access.CurrentUserId is not Guid userId || !await access.CanReadZevAsync(zevId, cancellationToken))
                return Results.NotFound();
            var context = await effectiveAccess.GetPermissionContextAsync(userId, zevId, cancellationToken);
            return Results.Ok(new { context.Roles, context.IsOwner, context.IsStaff, context.Sources,
                context.Permissions, modules = GranularPermissions.Modules });
        });

        api.MapGet("/zevs/{zevId:guid}/access/users", async (Guid zevId, string? search,
            TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Access.Manage", cancellationToken)) return Results.NotFound();
            var zev = await database.Zevs.AsNoTracking().Where(item => item.Id == zevId && item.IsActive)
                .Select(item => new { item.OrganizationId }).SingleOrDefaultAsync(cancellationToken);
            if (zev is null) return Results.NotFound();
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var query = database.Users.AsNoTracking().Where(item => item.IsActive && !item.IsSystemAdministrator
                && (database.Memberships.Any(membership => membership.UserId == item.Id && membership.IsActive
                    && membership.OrganizationId == zev.OrganizationId && (membership.ZevId == zevId
                        || membership.ZevId == null && (membership.Role == MembershipRole.OrganizationAdministrator
                            || membership.Role == MembershipRole.Accountant)))
                    || database.Ownerships.Any(ownership => ownership.ZevId == zevId
                        && ownership.Person.UserId == item.Id && ownership.Person.IsActive
                        && ownership.Person.UserLinkStatus == UserPersonLinkStatus.Verified
                        && ownership.ValidFrom <= today && (ownership.ValidTo == null || ownership.ValidTo > today))));
            if (!string.IsNullOrWhiteSpace(search)) query = query.Where(item => item.Email.Contains(search.Trim()));
            var users = await query.OrderBy(item => item.Email).Take(100)
                .Select(item => new { item.Id, item.Email, item.IsActive, item.IsSystemAdministrator })
                .ToListAsync(cancellationToken);
            return Results.Ok(users);
        });

        api.MapGet("/zevs/{zevId:guid}/access/users/{targetUserId:guid}/permissions", async (Guid zevId,
            Guid targetUserId, TenantAccess access, EffectiveAccessService effectiveAccess,
            ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Access.Manage", cancellationToken)
                || !await database.Users.AnyAsync(item => item.Id == targetUserId && item.IsActive
                    && !item.IsSystemAdministrator, cancellationToken)) return Results.NotFound();
            return Results.Ok(await effectiveAccess.GetPermissionContextAsync(targetUserId, zevId, cancellationToken));
        });

        api.MapPut("/zevs/{zevId:guid}/access/users/{targetUserId:guid}/permissions", async (Guid zevId,
            Guid targetUserId, SavePermissionMatrixRequest request, TenantAccess access,
            EffectiveAccessService effectiveAccess, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (access.CurrentUserId is not Guid actorId
                || !await access.HasPermissionAsync(zevId, "Access.Manage", cancellationToken)) return Results.NotFound();
            if (request.Changes is null || request.Changes.Count > GranularPermissions.Keys.Length
                || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500
                || request.Changes.Any(item => !GranularPermissions.IsKnown(item.Key)
                    || item.Effect is not (null or "Grant" or "Deny"))
                || request.Changes.Select(item => item.Key).Distinct(StringComparer.Ordinal).Count() != request.Changes.Count)
                return Results.BadRequest();
            var target = await database.Users.AsNoTracking().Where(item => item.Id == targetUserId && item.IsActive)
                .Select(item => new { item.IsSystemAdministrator }).SingleOrDefaultAsync(cancellationToken);
            if (target is null || target.IsSystemAdministrator) return Results.NotFound();
            var systemAdministrator = await access.IsSystemAdministratorAsync(actorId, cancellationToken);
            if (!systemAdministrator && actorId == targetUserId) return Results.NotFound();
            var zev = await database.Zevs.AsNoTracking().Where(item => item.Id == zevId && item.IsActive
                && item.Organization.IsActive).Select(item => new { item.OrganizationId })
                .SingleOrDefaultAsync(cancellationToken);
            if (zev is null) return Results.NotFound();
            if (!systemAdministrator
                && await database.Memberships.AnyAsync(item => item.UserId == targetUserId
                    && item.OrganizationId == zev.OrganizationId && item.Role == MembershipRole.OrganizationAdministrator
                    && item.ZevId == null && item.IsActive, cancellationToken)) return Results.NotFound();
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var actorContext = await effectiveAccess.GetPermissionContextAsync(actorId, zevId, cancellationToken);
            if (!actorContext.IsStaff || !actorContext.Permissions.Any(item => item.Key == "Access.Manage" && item.Effective))
                return Results.NotFound();
            var targetContext = await effectiveAccess.GetPermissionContextAsync(targetUserId, zevId, cancellationToken);
            if (request.Changes.Any(change =>
            {
                var targetPermission = targetContext.Permissions.Single(item => item.Key == change.Key);
                var resultingGrant = change.Effect == "Grant" || change.Effect is null && targetPermission.RoleDefault;
                return resultingGrant && !actorContext.Permissions.Any(item => item.Key == change.Key && item.Effective);
            })) return Results.Forbid();
            var now = DateTimeOffset.UtcNow;
            var keys = request.Changes.Select(item => item.Key).ToArray();
            var current = await database.AccessOverrides.Where(item => item.UserId == targetUserId
                && item.ZevId == zevId && keys.Contains(item.PermissionKey) && item.RevokedUtc == null
                && item.ValidFromUtc <= now && (item.ValidToUtc == null || item.ValidToUtc > now))
                .ToListAsync(cancellationToken);
            foreach (var change in request.Changes)
            {
                var active = current.Where(item => item.PermissionKey == change.Key).ToList();
                if (active.Count == 1 && active[0].Effect.ToString() == change.Effect) continue;
                foreach (var item in active)
                {
                    item.RevokedUtc = now; item.RevokedByUserId = actorId; item.RevokeReason = request.Reason.Trim();
                }
                if (change.Effect is null) continue;
                database.AccessOverrides.Add(new AccessOverride { UserId = targetUserId,
                    OrganizationId = zev.OrganizationId, ZevId = zevId, PermissionKey = change.Key,
                    Effect = Enum.Parse<AccessEffect>(change.Effect), ValidFromUtc = now,
                    Reason = request.Reason.Trim(), CreatedByUserId = actorId });
            }
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(await effectiveAccess.GetPermissionContextAsync(targetUserId, zevId, cancellationToken));
        });

        var group = api.MapGroup("/zevs/{zevId:guid}/access/overrides");
        group.MapGet("", async (Guid zevId, TenantAccess access, ZevDbContext database,
            CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Access.Manage", cancellationToken)) return Results.NotFound();
            var rows = await database.AccessOverrides.AsNoTracking().Where(item => item.ZevId == zevId)
                .OrderByDescending(item => item.CreatedUtc).Take(200)
                .Select(item => new { item.Id, item.UserId, item.ZevId, item.PermissionKey,
                    effect = item.Effect.ToString(), item.ValidFromUtc, item.ValidToUtc, item.Reason,
                    item.CreatedByUserId, item.CreatedUtc, item.RevokedByUserId, item.RevokedUtc, item.RevokeReason })
                .ToListAsync(cancellationToken);
            return Results.Ok(rows);
        });

        group.MapPost("", async (Guid zevId, CreateAccessOverrideRequest request, TenantAccess access,
            EffectiveAccessService effectiveAccess, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (access.CurrentUserId is not Guid actorId
                || !AccessPermissions.Overrideable.Contains(request.PermissionKey)
                || !Enum.TryParse<AccessEffect>(request.Effect, true, out var effect) || !Enum.IsDefined(effect)
                || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500
                || request.ValidToUtc <= request.ValidFromUtc) return Results.BadRequest();
            var rights = await effectiveAccess.GetEffectiveZevAccess(actorId, zevId, cancellationToken);
            if (!await access.HasPermissionAsync(zevId, "Access.Manage", cancellationToken)
                || !CanDelegate(rights, request.PermissionKey)) return Results.NotFound();
            var target = await database.Users.AsNoTracking().Where(item => item.Id == request.UserId && item.IsActive)
                .Select(item => new { item.IsSystemAdministrator }).SingleOrDefaultAsync(cancellationToken);
            if (target is null || target.IsSystemAdministrator) return Results.NotFound();
            var organizationId = await database.Zevs.Where(item => item.Id == zevId)
                .Select(item => item.OrganizationId).SingleAsync(cancellationToken);
            var systemAdministrator = await access.IsSystemAdministratorAsync(actorId, cancellationToken);
            // Same rules as the matrix: nobody changes their own rights or an organization administrator's.
            if (!systemAdministrator && actorId == request.UserId) return Results.NotFound();
            if (!systemAdministrator
                && await database.Memberships.AnyAsync(item => item.UserId == request.UserId
                    && item.OrganizationId == organizationId && item.Role == MembershipRole.OrganizationAdministrator
                    && item.ZevId == null && item.IsActive, cancellationToken)) return Results.NotFound();
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var hasOwnership = await database.Ownerships.AnyAsync(item => item.ZevId == zevId
                && item.Person.UserId == request.UserId && item.Person.IsActive
                && item.Person.UserLinkStatus == UserPersonLinkStatus.Verified
                && item.ValidFrom <= today && (item.ValidTo == null || item.ValidTo > today), cancellationToken);
            if (effect == AccessEffect.Grant && request.PermissionKey == AccessPermissions.Read
                && !hasOwnership && request.ValidToUtc is null)
                return Results.BadRequest(new { error = "Korisnik nema aktivno vlasništvo u ovom ZEV-u. Za ručni pristup unesite datum isteka i razlog." });
            var item = new AccessOverride { UserId = request.UserId, OrganizationId = organizationId,
                ZevId = zevId, PermissionKey = request.PermissionKey, Effect = effect,
                ValidFromUtc = request.ValidFromUtc, ValidToUtc = request.ValidToUtc,
                Reason = request.Reason.Trim(), CreatedByUserId = actorId };
            database.AccessOverrides.Add(item);
            await database.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/zevs/{zevId}/access/overrides/{item.Id}", new
            {
                item.Id, source = "MANUAL_OVERRIDE", warning = !hasOwnership && effect == AccessEffect.Grant
                    ? "Korisnik nema aktivno vlasništvo u ovom ZEV-u. Ovo pravo nije izvedeno iz vlasništva." : null
            });
        });

        group.MapPost("/{overrideId:guid}/revoke", async (Guid zevId, Guid overrideId,
            RevokeAccessOverrideRequest? request, TenantAccess access, EffectiveAccessService effectiveAccess,
            ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (access.CurrentUserId is not Guid actorId) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(request?.Reason) || request.Reason.Trim().Length > 500)
                return Results.BadRequest(new { error = "Razlog opoziva je obavezan (najviše 500 znakova)." });
            var item = await database.AccessOverrides.SingleOrDefaultAsync(value => value.Id == overrideId
                && value.ZevId == zevId, cancellationToken);
            if (item is null || item.RevokedUtc is not null) return Results.NotFound();
            var rights = await effectiveAccess.GetEffectiveZevAccess(actorId, zevId, cancellationToken);
            if (!await access.HasPermissionAsync(zevId, "Access.Manage", cancellationToken)
                || !CanDelegate(rights, item.PermissionKey)) return Results.NotFound();
            var systemAdministrator = await access.IsSystemAdministratorAsync(actorId, cancellationToken);
            if (!systemAdministrator && actorId == item.UserId) return Results.NotFound();
            if (!systemAdministrator
                && await database.Memberships.AnyAsync(membership => membership.UserId == item.UserId
                    && membership.OrganizationId == item.OrganizationId
                    && membership.Role == MembershipRole.OrganizationAdministrator
                    && membership.ZevId == null && membership.IsActive, cancellationToken)) return Results.NotFound();
            item.RevokedByUserId = actorId;
            item.RevokedUtc = DateTimeOffset.UtcNow;
            item.RevokeReason = request.Reason.Trim();
            await database.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });
        return api;
    }

    private static bool CanDelegate(EffectiveZevAccess rights, string permission) => permission switch
    {
        AccessPermissions.Read => rights.CanRead,
        AccessPermissions.Finance => rights.CanManageFinances,
        AccessPermissions.Legal => rights.CanManageLegalCases,
        AccessPermissions.Maintenance => rights.CanManageMaintenance,
        _ => false
    };
}

public sealed record CreateAccessOverrideRequest(Guid UserId, string PermissionKey, string Effect,
    DateTimeOffset ValidFromUtc, DateTimeOffset? ValidToUtc, string Reason);
public sealed record RevokeAccessOverrideRequest(string? Reason);
public sealed record PermissionChange(string Key, string? Effect);
public sealed record SavePermissionMatrixRequest(List<PermissionChange> Changes, string Reason);
