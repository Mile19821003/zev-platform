using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Security;

namespace Zev.Api.Endpoints;

public static class AdminAccessEndpoints
{
    public static RouteGroupBuilder MapAccessEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/roles", async (ZevDbContext database, CancellationToken cancellationToken) =>
        {
            var grants = await database.RolePermissions.AsNoTracking().ToListAsync(cancellationToken);
            var roles = Enum.GetValues<MembershipRole>().Select(role => new
            {
                name = role.ToString(),
                scope = role == MembershipRole.OrganizationAdministrator ? "organization"
                    : role == MembershipRole.Owner ? "ownership"
                    : role == MembershipRole.Accountant ? "organization-or-zev" : "zev",
                permissions = grants.Where(grant => grant.Role == role).Select(grant => grant.PermissionKey).Order().ToArray()
            });
            return Results.Ok(new { systemAdministrator = new { name = "SystemAdministrator", scope = "system" }, roles });
        });

        group.MapGet("/users", async (int? page, int? pageSize, string? search, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
            var query = database.Users.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(item => item.Email.Contains(term));
            }

            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query.OrderBy(item => item.Email).ThenBy(item => item.Id)
                .Skip((currentPage - 1) * currentSize).Take(currentSize).ToListAsync(cancellationToken);
            return Results.Ok(new { page = currentPage, pageSize = currentSize, totalCount, items = items.Select(UserView) });
        });

        group.MapPatch("/users/{userId:guid}/status", async (Guid userId, UpdateStatusRequest request, TenantAccess access,
            ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!AdminValidation.TryRowVersion(request.RowVersion, out var rowVersion)) return Results.BadRequest();
            var user = await database.Users.SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);
            if (user is null) return Results.NotFound();
            if (!request.IsActive && (user.IsSystemAdministrator || access.CurrentUserId == userId)) return Results.Conflict();
            database.Entry(user).Property(item => item.RowVersion).OriginalValue = rowVersion;
            user.IsActive = request.IsActive;
            if (!request.IsActive)
            {
                var linkedPeople = await database.People.Where(item => item.UserId == userId
                    && item.UserLinkStatus == UserPersonLinkStatus.Verified).ToListAsync(cancellationToken);
                foreach (var person in linkedPeople)
                {
                    person.UserLinkStatus = UserPersonLinkStatus.Revoked;
                    person.UserLinkReviewedByUserId = access.CurrentUserId;
                    person.UserLinkReviewedUtc = DateTimeOffset.UtcNow;
                }
            }
            try
            {
                await database.SaveChangesAsync(cancellationToken);
                return Results.Ok(UserView(user));
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new { error = "User was changed by another request." });
            }
        });

        group.MapGet("/memberships", async (int? page, int? pageSize, Guid? userId, Guid? organizationId,
            Guid? zevId, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
            var query = database.Memberships.AsNoTracking().Include(item => item.User)
                .Include(item => item.Organization).Include(item => item.Zev).AsQueryable();
            if (userId is Guid selectedUser) query = query.Where(item => item.UserId == selectedUser);
            if (organizationId is Guid selectedOrganization) query = query.Where(item => item.OrganizationId == selectedOrganization);
            if (zevId is Guid selectedZev) query = query.Where(item => item.ZevId == selectedZev);
            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query.OrderBy(item => item.User.Email).ThenBy(item => item.Organization.Name).ThenBy(item => item.Id)
                .Skip((currentPage - 1) * currentSize).Take(currentSize).ToListAsync(cancellationToken);
            return Results.Ok(new { page = currentPage, pageSize = currentSize, totalCount, items = items.Select(MembershipView) });
        });

        group.MapGet("/derived-owners", async (Guid? zevId, int? page, int? pageSize,
            ZevDbContext database, CancellationToken cancellationToken) =>
        {
            var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var query = database.Ownerships.AsNoTracking().Where(item => item.ValidFrom <= today
                && (item.ValidTo == null || item.ValidTo > today) && item.PropertyUnit.IsActive
                && item.Person.IsActive && item.Person.UserId != null
                && item.Person.UserLinkStatus == UserPersonLinkStatus.Verified
                && (zevId == null || item.ZevId == zevId));
            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query.OrderBy(item => item.ZevId).ThenBy(item => item.PropertyUnit.Code)
                .Skip((currentPage - 1) * currentSize).Take(currentSize)
                .Select(item => new { item.ZevId, item.PropertyUnitId, unitCode = item.PropertyUnit.Code,
                    item.PersonId, personName = item.Person.DisplayName, item.Person.UserId,
                    userEmail = item.Person.User!.Email, item.Share, role = "Owner", source = "OWNERSHIP" })
                .ToListAsync(cancellationToken);
            return Results.Ok(new { page = currentPage, pageSize = currentSize, totalCount, items });
        });

        // "Uredi" on a manual role: user, organization, ZEV and role, under the same rules as a new assignment.
        group.MapPut("/memberships/{membershipId:guid}", async (Guid membershipId, UpdateMembershipRequest request,
            ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!AdminValidation.TryRowVersion(request.RowVersion, out var rowVersion)) return Results.BadRequest();
            var membership = await database.Memberships.SingleOrDefaultAsync(item => item.Id == membershipId, cancellationToken);
            if (membership is null) return Results.NotFound();
            var (problem, role) = await MembershipRules.ValidateAsync(database, request.UserId, request.OrganizationId, request.ZevId,
                request.Role, membershipId, cancellationToken);
            if (problem is not null) return problem;
            database.Entry(membership).Property(item => item.RowVersion).OriginalValue = rowVersion;
            membership.UserId = request.UserId;
            membership.OrganizationId = request.OrganizationId;
            membership.ZevId = request.ZevId;
            membership.Role = role;
            try { await database.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Results.Conflict(new { error = "Uloga je u međuvremenu izmijenjena. Osvježite i pokušajte ponovo." }); }
            catch (DbUpdateException) { return Results.Conflict(new { error = "Korisnik već ima ovu ulogu u odabranoj organizaciji / ZEV-u." }); }
            var saved = await database.Memberships.AsNoTracking().Include(item => item.User).Include(item => item.Organization)
                .Include(item => item.Zev).SingleAsync(item => item.Id == membershipId, cancellationToken);
            return Results.Ok(MembershipView(saved));
        });

        group.MapPatch("/memberships/{membershipId:guid}/status", async (Guid membershipId, UpdateStatusRequest request,
            ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!AdminValidation.TryRowVersion(request.RowVersion, out var rowVersion)) return Results.BadRequest();
            var membership = await database.Memberships.Include(item => item.User).Include(item => item.Organization)
                .Include(item => item.Zev).SingleOrDefaultAsync(item => item.Id == membershipId, cancellationToken);
            if (membership is null) return Results.NotFound();
            if (request.IsActive && (!membership.User.IsActive || !membership.Organization.IsActive
                || (membership.Zev is not null && !membership.Zev.IsActive))) return Results.Conflict();
            database.Entry(membership).Property(item => item.RowVersion).OriginalValue = rowVersion;
            membership.IsActive = request.IsActive;
            try
            {
                await database.SaveChangesAsync(cancellationToken);
                return Results.Ok(MembershipView(membership));
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new { error = "Membership was changed by another request." });
            }
        });

        return group;
    }

    public sealed record UpdateMembershipRequest(Guid UserId, Guid OrganizationId, Guid? ZevId, string Role, string RowVersion);

    private static object UserView(AppUser item) => new
    {
        item.Id, item.Email, item.IsActive, item.IsSystemAdministrator, item.MustChangePassword,
        rowVersion = Convert.ToBase64String(item.RowVersion)
    };

    private static object MembershipView(Membership item) => new
    {
        item.Id, item.UserId, userEmail = item.User.Email, item.OrganizationId,
        organizationName = item.Organization.Name, item.ZevId, zevName = item.Zev?.Name,
        role = item.Role.ToString(), item.IsActive, rowVersion = Convert.ToBase64String(item.RowVersion)
    };
}
