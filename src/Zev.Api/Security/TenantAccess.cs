using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;

namespace Zev.Api.Security;

public sealed class TenantAccess(ZevDbContext database, IHttpContextAccessor httpContextAccessor,
    EffectiveAccessService effectiveAccess)
{
    public Guid? CurrentUserId => Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirstValue("sub"), out var userId) ? userId : null;

    public Task<bool> IsSystemAdministratorAsync(Guid userId, CancellationToken cancellationToken) =>
        database.Users.AnyAsync(item => item.Id == userId && item.IsActive && item.IsSystemAdministrator, cancellationToken);

    public async Task<bool> CanReadOrganizationAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        if (CurrentUserId is not Guid userId || !await database.Users.AnyAsync(item => item.Id == userId
            && item.IsActive, cancellationToken) || !await database.Organizations.AnyAsync(item => item.Id == organizationId
            && item.IsActive, cancellationToken)) return false;
        if (await IsSystemAdministratorAsync(userId, cancellationToken)) return true;
        return await database.Memberships.AnyAsync(item => item.UserId == userId && item.OrganizationId == organizationId
            && item.IsActive && item.ZevId == null && item.Role == MembershipRole.OrganizationAdministrator,
            cancellationToken);
    }

    // Read-only access to organization processes (hodogram/RACI): organization readers, plus building
    // representatives of a ZEV in that organization they can currently read.
    public async Task<bool> CanViewOrganizationProcessesAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        if (await CanReadOrganizationAsync(organizationId, cancellationToken)) return true;
        if (CurrentUserId is not Guid userId) return false;
        var zevIds = await database.Memberships.AsNoTracking().Where(item => item.UserId == userId && item.IsActive
                && item.OrganizationId == organizationId && item.Role == MembershipRole.BuildingRepresentative
                && item.ZevId != null)
            .Select(item => item.ZevId!.Value).Distinct().ToListAsync(cancellationToken);
        foreach (var zevId in zevIds)
            if (await CanReadZevAsync(zevId, cancellationToken)) return true;
        return false;
    }

    public async Task<bool> CanReadZevAsync(Guid zevId, CancellationToken cancellationToken) =>
        CurrentUserId is Guid userId && (await effectiveAccess.GetEffectiveZevAccess(userId, zevId, cancellationToken)).CanRead;

    public async Task<bool> CanManageFinancesAsync(Guid zevId, CancellationToken cancellationToken) =>
        CurrentUserId is Guid userId && (await effectiveAccess.GetEffectiveZevAccess(userId, zevId, cancellationToken)).CanManageFinances;

    public async Task<bool> CanManageLegalCasesAsync(Guid zevId, CancellationToken cancellationToken) =>
        CurrentUserId is Guid userId && (await effectiveAccess.GetEffectiveZevAccess(userId, zevId, cancellationToken)).CanManageLegalCases;

    public async Task<bool> CanManageMaintenanceAsync(Guid zevId, CancellationToken cancellationToken) =>
        CurrentUserId is Guid userId && (await effectiveAccess.GetEffectiveZevAccess(userId, zevId, cancellationToken)).CanManageMaintenance;

    // Authorization policy for routes inside a ZEV (see ACCESS_CONTROL_AND_PRIVACY.md, "403 ili 404"):
    // null when allowed; 404 when the caller cannot read the ZEV at all (another tenant or an unknown ZEV),
    // so foreign resources are not revealed; 403 when the caller can read the ZEV but lacks a permission.
    public async Task<IResult?> RequirePermissionAsync(Guid zevId, string permissionKey,
        CancellationToken cancellationToken, bool staffOnly = true) =>
        await HasPermissionAsync(zevId, permissionKey, cancellationToken, staffOnly) ? null
            : await DeniedAsync(zevId, cancellationToken);

    public async Task<IResult?> RequireAllPermissionsAsync(Guid zevId, CancellationToken cancellationToken,
        params string[] permissionKeys)
    {
        foreach (var key in permissionKeys)
            if (!await HasPermissionAsync(zevId, key, cancellationToken)) return await DeniedAsync(zevId, cancellationToken);
        return null;
    }

    // The refusal for a caller who passed no permission check: 403 inside a readable ZEV, otherwise 404.
    public async Task<IResult> DeniedAsync(Guid zevId, CancellationToken cancellationToken) =>
        await CanReadZevAsync(zevId, cancellationToken) ? Results.Forbid() : Results.NotFound();

    public async Task<bool> HasPermissionAsync(Guid zevId, string permissionKey, CancellationToken cancellationToken,
        bool staffOnly = true) => CurrentUserId is Guid userId
            && await effectiveAccess.HasPermissionAsync(userId, zevId, permissionKey, cancellationToken, staffOnly);

    public async Task<bool> CanAccessOwnPersonAsync(Guid zevId, Guid personId, DateOnly? recordDate,
        CancellationToken cancellationToken) => CurrentUserId is Guid userId
            && await effectiveAccess.CanAccessOwnPersonAsync(userId, zevId, personId, recordDate, cancellationToken);

    public async Task<bool> CanReadHistoricalPersonAsync(Guid zevId, Guid personId, DateOnly? recordDate,
        CancellationToken cancellationToken) => CurrentUserId is Guid userId
            && await effectiveAccess.CanReadHistoricalPersonAsync(userId, zevId, personId, recordDate, cancellationToken);

    public async Task<bool> CanReportTicketAsync(Guid zevId, Guid? unitId, CancellationToken cancellationToken)
    {
        if (!await CanReadZevAsync(zevId, cancellationToken) || CurrentUserId is not Guid userId) return false;
        // Common parts (no unit): anyone who manages maintenance, including the building representative.
        if (unitId is not Guid propertyUnitId) return await CanManageMaintenanceAsync(zevId, cancellationToken);
        // A specific unit: maintenance staff with edit rights, otherwise only its verified owner or occupant.
        if (await CanManageMaintenanceAsync(zevId, cancellationToken)
            && await HasPermissionAsync(zevId, "Maintenance.Edit", cancellationToken)) return true;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return await effectiveAccess.CanAccessOwnUnitAsync(userId, zevId, propertyUnitId, cancellationToken)
            || await database.Occupancies.AnyAsync(item => item.ZevId == zevId && item.PropertyUnitId == propertyUnitId
                && item.ValidFrom <= today && (item.ValidTo == null || item.ValidTo > today)
                && item.Person.UserId == userId && item.Person.IsActive
                && item.Person.UserLinkStatus == UserPersonLinkStatus.Verified, cancellationToken);
    }
}
