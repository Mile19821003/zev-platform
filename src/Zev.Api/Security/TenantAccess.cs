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

    public async Task<bool> CanReadZevAsync(Guid zevId, CancellationToken cancellationToken) =>
        CurrentUserId is Guid userId && (await effectiveAccess.GetEffectiveZevAccess(userId, zevId, cancellationToken)).CanRead;

    public async Task<bool> CanManageFinancesAsync(Guid zevId, CancellationToken cancellationToken) =>
        CurrentUserId is Guid userId && (await effectiveAccess.GetEffectiveZevAccess(userId, zevId, cancellationToken)).CanManageFinances;

    public async Task<bool> CanManageLegalCasesAsync(Guid zevId, CancellationToken cancellationToken) =>
        CurrentUserId is Guid userId && (await effectiveAccess.GetEffectiveZevAccess(userId, zevId, cancellationToken)).CanManageLegalCases;

    public async Task<bool> CanManageMaintenanceAsync(Guid zevId, CancellationToken cancellationToken) =>
        CurrentUserId is Guid userId && (await effectiveAccess.GetEffectiveZevAccess(userId, zevId, cancellationToken)).CanManageMaintenance;

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
        if (await CanManageMaintenanceAsync(zevId, cancellationToken)) return true;
        if (unitId is not Guid propertyUnitId) return false;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return await effectiveAccess.CanAccessOwnUnitAsync(userId, zevId, propertyUnitId, cancellationToken)
            || await database.Occupancies.AnyAsync(item => item.ZevId == zevId && item.PropertyUnitId == propertyUnitId
                && item.ValidFrom <= today && (item.ValidTo == null || item.ValidTo > today)
                && item.Person.UserId == userId && item.Person.IsActive
                && item.Person.UserLinkStatus == UserPersonLinkStatus.Verified, cancellationToken);
    }
}
