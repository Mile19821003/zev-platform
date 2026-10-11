using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;

namespace Zev.Api.Security;

// Rules for manual role assignments (memberships), shared by create and update so both accept the same data.
public static class MembershipRules
{
    // 400 for an invalid role / ZEV combination, 404 for an unknown or inactive user, organization or ZEV,
    // 409 when the same assignment already exists (another row than `exceptId`).
    public static async Task<(IResult? Problem, MembershipRole Role)> ValidateAsync(ZevDbContext database, Guid userId,
        Guid organizationId, Guid? zevId, string? roleText, Guid? exceptId, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<MembershipRole>(roleText, true, out var role) || !Enum.IsDefined(role)
            || role == MembershipRole.Owner
            || (zevId is null && role is not (MembershipRole.OrganizationAdministrator or MembershipRole.Accountant))
            || (zevId is not null && role == MembershipRole.OrganizationAdministrator))
            return (Results.BadRequest(), default);
        if (!await database.Users.AnyAsync(item => item.Id == userId && item.IsActive, cancellationToken)
            || !await database.Organizations.AnyAsync(item => item.Id == organizationId && item.IsActive, cancellationToken)
            || (zevId is Guid zev && !await database.Zevs.AnyAsync(item => item.Id == zev
                && item.OrganizationId == organizationId && item.IsActive, cancellationToken)))
            return (Results.NotFound(), role);
        if (await database.Memberships.AnyAsync(item => item.Id != exceptId && item.UserId == userId
                && item.OrganizationId == organizationId && item.ZevId == zevId && item.Role == role, cancellationToken))
            return (Results.Conflict(new { error = "Korisnik već ima ovu ulogu u odabranoj organizaciji / ZEV-u." }), role);
        return (null, role);
    }
}
