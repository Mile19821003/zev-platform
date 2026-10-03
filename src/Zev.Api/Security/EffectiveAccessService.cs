using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;

namespace Zev.Api.Security;

public static class AccessPermissions
{
    public const string Read = "zev.read";
    public const string Finance = "finance.manage";
    public const string Legal = "legal.manage";
    public const string Maintenance = "maintenance.manage";
    public static readonly string[] Overrideable = [Read, Finance, Legal, Maintenance];
}

public sealed record EffectiveZevAccess(bool CanRead, bool CanManageFinances, bool CanManageLegalCases,
    bool CanManageMaintenance, bool IsOwner, string[] Sources);

public sealed class EffectiveAccessService(ZevDbContext database, IConfiguration configuration)
{
    public async Task<PermissionContext> GetPermissionContextAsync(Guid userId, Guid zevId,
        CancellationToken cancellationToken)
    {
        var access = await GetEffectiveZevAccess(userId, zevId, cancellationToken);
        var isAdministrator = await database.Users.AsNoTracking().AnyAsync(item => item.Id == userId
            && item.IsActive && item.IsSystemAdministrator, cancellationToken);
        var zev = await database.Zevs.AsNoTracking().Where(item => item.Id == zevId && item.IsActive
            && item.Organization.IsActive).Select(item => new { item.OrganizationId }).SingleOrDefaultAsync(cancellationToken);
        if (zev is null) return new([], false, false, [], []);
        MembershipRole[] roles = isAdministrator ? [] : await database.Memberships.AsNoTracking()
            .Where(item => item.UserId == userId && item.IsActive && item.OrganizationId == zev.OrganizationId
                && item.Role != MembershipRole.Owner && (item.ZevId == zevId || item.ZevId == null
                    && (item.Role == MembershipRole.OrganizationAdministrator || item.Role == MembershipRole.Accountant)))
            .Select(item => item.Role).Distinct().ToArrayAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var overrides = await database.AccessOverrides.AsNoTracking().Where(item => item.UserId == userId
            && item.ZevId == zevId && item.RevokedUtc == null && item.ValidFromUtc <= now
            && (item.ValidToUtc == null || item.ValidToUtc > now))
            .Select(item => new { item.PermissionKey, item.Effect }).ToListAsync(cancellationToken);
        var states = GranularPermissions.Keys.Select(key =>
        {
            var inherited = isAdministrator || roles.Any(role => GranularPermissions.RoleDefault(role, key))
                || access.IsOwner && GranularPermissions.RoleDefault(MembershipRole.Owner, key)
                || !access.IsOwner && roles.Length == 0 && access.CanRead
                    && access.Sources.Contains("MANUAL_OVERRIDE")
                    && key is ("Overview.View" or "Community.View" or "Documents.View" or "Documents.Download");
            var denied = overrides.Any(item => item.PermissionKey == key && item.Effect == AccessEffect.Deny);
            var granted = overrides.Any(item => item.PermissionKey == key && item.Effect == AccessEffect.Grant);
            return new PermissionState(key, inherited, denied ? "Deny" : granted ? "Grant" : null,
                access.CanRead && (isAdministrator || !denied && (inherited || granted)));
        }).ToArray();
        return new(roles.Select(item => item.ToString()).ToArray(), access.IsOwner, isAdministrator || roles.Length > 0,
            access.Sources, states);
    }

    public async Task<bool> HasPermissionAsync(Guid userId, Guid zevId, string permissionKey,
        CancellationToken cancellationToken, bool staffOnly = true)
    {
        if (!GranularPermissions.IsKnown(permissionKey)) return false;
        var context = await GetPermissionContextAsync(userId, zevId, cancellationToken);
        return (!staffOnly || context.IsStaff) && context.Permissions.Any(item => item.Key == permissionKey && item.Effective);
    }

    public async Task<EffectiveZevAccess> GetEffectiveZevAccess(Guid userId, Guid zevId,
        CancellationToken cancellationToken)
    {
        var empty = new EffectiveZevAccess(false, false, false, false, false, []);
        var user = await database.Users.AsNoTracking().Where(item => item.Id == userId && item.IsActive)
            .Select(item => new { item.IsSystemAdministrator }).SingleOrDefaultAsync(cancellationToken);
        var zev = await database.Zevs.AsNoTracking().Where(item => item.Id == zevId && item.IsActive
            && item.Organization.IsActive).Select(item => new { item.OrganizationId }).SingleOrDefaultAsync(cancellationToken);
        if (user is null || zev is null) return empty;
        if (user.IsSystemAdministrator) return new(true, true, true, true, false, ["SYSTEM_ROLE"]);

        var memberships = await database.Memberships.AsNoTracking().Where(item => item.UserId == userId
            && item.IsActive && item.OrganizationId == zev.OrganizationId
            && item.Role != MembershipRole.Owner
            && (item.ZevId == zevId || (item.ZevId == null
                && (item.Role == MembershipRole.OrganizationAdministrator || item.Role == MembershipRole.Accountant))))
            .Select(item => new { item.Role, item.ZevId }).ToListAsync(cancellationToken);
        var organizationAdministrator = memberships.Any(item => item.Role == MembershipRole.OrganizationAdministrator
            && item.ZevId == null);
        var manager = memberships.Any(item => item.Role == MembershipRole.Manager && item.ZevId == zevId);
        var accountant = memberships.Any(item => item.Role == MembershipRole.Accountant
            && (item.ZevId == zevId || item.ZevId == null));
        var representative = memberships.Any(item => item.Role == MembershipRole.BuildingRepresentative
            && item.ZevId == zevId);
        var membershipRoles = memberships.Select(item => item.Role).Distinct().ToArray();
        var roleGrants = await database.RolePermissions.AsNoTracking()
            .Where(item => membershipRoles.Contains(item.Role))
            .Select(item => item.PermissionKey).Distinct().ToListAsync(cancellationToken);
        bool RoleGrants(string permission) => roleGrants.Contains(permission);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var owner = await database.Ownerships.AsNoTracking().AnyAsync(item => item.ZevId == zevId
            && item.ValidFrom <= today && (item.ValidTo == null || item.ValidTo > today)
            && item.PropertyUnit.IsActive && item.Person.IsActive && item.Person.UserId == userId
            && item.Person.UserLinkStatus == UserPersonLinkStatus.Verified, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var overrides = await database.AccessOverrides.AsNoTracking().Where(item => item.UserId == userId
            && item.ZevId == zevId && item.RevokedUtc == null && item.ValidFromUtc <= now
            && (item.ValidToUtc == null || item.ValidToUtc > now))
            .Select(item => new { item.PermissionKey, item.Effect }).ToListAsync(cancellationToken);
        bool Allowed(string permission, bool roleGrant) =>
            !overrides.Any(item => item.PermissionKey == permission && item.Effect == AccessEffect.Deny)
            && (roleGrant || overrides.Any(item => item.PermissionKey == permission && item.Effect == AccessEffect.Grant));

        var read = Allowed(AccessPermissions.Read, RoleGrants(AccessPermissions.Read) || owner);
        var sources = new List<string>();
        if (organizationAdministrator) sources.Add("ORG_ROLE");
        if (manager || accountant || representative) sources.Add("ZEV_MEMBERSHIP");
        if (owner) sources.Add("OWNERSHIP");
        if (overrides.Count > 0) sources.Add("MANUAL_OVERRIDE");
        return new(read,
            read && Allowed(AccessPermissions.Finance, RoleGrants(AccessPermissions.Finance)),
            read && Allowed(AccessPermissions.Legal, RoleGrants(AccessPermissions.Legal)),
            read && Allowed(AccessPermissions.Maintenance, RoleGrants(AccessPermissions.Maintenance)),
            read && owner, sources.ToArray());
    }

    public async Task<bool> CanAccessOwnPersonAsync(Guid userId, Guid zevId, Guid personId,
        DateOnly? recordDate, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var historical = configuration.GetValue<bool>("Privacy:HistoricalSelfAccess");
        return await database.Ownerships.AsNoTracking().AnyAsync(item => item.ZevId == zevId
            && item.PersonId == personId && item.Person.UserId == userId && item.Person.IsActive
            && item.Person.UserLinkStatus == UserPersonLinkStatus.Verified
            && item.ValidFrom <= today
            && ((item.PropertyUnit.IsActive && (item.ValidTo == null || item.ValidTo > today))
                || (historical && recordDate != null && item.ValidFrom <= recordDate
                    && (item.ValidTo == null || item.ValidTo > recordDate))), cancellationToken);
    }

    public async Task<bool> CanReadHistoricalPersonAsync(Guid userId, Guid zevId, Guid personId,
        DateOnly? recordDate, CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>("Privacy:HistoricalSelfAccess")) return false;
        var now = DateTimeOffset.UtcNow;
        if (!await database.Users.AnyAsync(item => item.Id == userId && item.IsActive, cancellationToken)
            || !await database.Zevs.AnyAsync(item => item.Id == zevId && item.IsActive && item.Organization.IsActive,
                cancellationToken)
            || await database.AccessOverrides.AnyAsync(item => item.UserId == userId && item.ZevId == zevId
                && item.PermissionKey == AccessPermissions.Read && item.Effect == AccessEffect.Deny
                && item.RevokedUtc == null && item.ValidFromUtc <= now
                && (item.ValidToUtc == null || item.ValidToUtc > now), cancellationToken)) return false;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        return await database.Ownerships.AnyAsync(item => item.ZevId == zevId && item.PersonId == personId
            && item.Person.UserId == userId && item.Person.IsActive
            && item.Person.UserLinkStatus == UserPersonLinkStatus.Verified
            && item.ValidFrom <= today && item.ValidTo != null && item.ValidTo <= today
            && (recordDate == null || item.ValidFrom <= recordDate && item.ValidTo > recordDate),
            cancellationToken);
    }

    public async Task<bool> HasHistoricalZevAccessAsync(Guid userId, Guid zevId,
        CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>("Privacy:HistoricalSelfAccess")) return false;
        var personIds = await database.People.AsNoTracking().Where(item => item.UserId == userId
            && item.IsActive && item.UserLinkStatus == UserPersonLinkStatus.Verified)
            .Select(item => item.Id).ToListAsync(cancellationToken);
        foreach (var personId in personIds)
            if (await CanReadHistoricalPersonAsync(userId, zevId, personId, null, cancellationToken)) return true;
        return false;
    }

    public async Task<bool> CanAccessOwnUnitAsync(Guid userId, Guid zevId, Guid unitId,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return await database.Ownerships.AsNoTracking().AnyAsync(item => item.ZevId == zevId
            && item.PropertyUnitId == unitId && item.ValidFrom <= today
            && (item.ValidTo == null || item.ValidTo > today) && item.PropertyUnit.IsActive
            && item.Person.IsActive && item.Person.UserId == userId
            && item.Person.UserLinkStatus == UserPersonLinkStatus.Verified, cancellationToken);
    }
}
