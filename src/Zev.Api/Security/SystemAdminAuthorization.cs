using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;

namespace Zev.Api.Security;

public sealed class SystemAdminRequirement : IAuthorizationRequirement;

public sealed class SystemAdminAuthorizationHandler(ZevDbContext database) : AuthorizationHandler<SystemAdminRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, SystemAdminRequirement requirement)
    {
        if (Guid.TryParse(context.User.FindFirst("sub")?.Value, out var userId)
            && await database.Users.AnyAsync(user => user.Id == userId && user.IsActive && user.IsSystemAdministrator))
        {
            context.Succeed(requirement);
        }
    }
}
