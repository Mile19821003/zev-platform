using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Lookups;
using Zev.Api.Security;

namespace Zev.Api.Endpoints;

public static class LookupEndpoints
{
    // Reading active items is open to every signed-in user (forms); managing the lists is for system administrators.
    public static RouteGroupBuilder MapLookupEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/lookups/{typeCode}", async (string typeCode, Guid? zevId, TenantAccess access,
            ZevDbContext database, CancellationToken cancellationToken) =>
        {
            var type = await database.LookupTypes.AsNoTracking().Where(item => item.Code == typeCode)
                .Select(item => new { item.Code, item.Name }).SingleOrDefaultAsync(cancellationToken);
            if (type is null) return Results.NotFound();
            // ZEV-specific items only for a ZEV the caller can read; another tenant's ZEV is not revealed.
            if (zevId is Guid zev && !await access.CanReadZevAsync(zev, cancellationToken)) return Results.NotFound();
            var items = await database.LookupItems.AsNoTracking().Where(item => item.LookupTypeCode == typeCode
                    && item.IsActive && (item.ZevId == null || zevId != null && item.ZevId == zevId))
                .OrderBy(item => item.SortOrder).ThenBy(item => item.Name)
                .Select(item => new { item.Id, item.Code, item.Name, item.SortOrder, item.IsDefault, item.ZevId })
                .ToListAsync(cancellationToken);
            return Results.Ok(new { type, items });
        });

        var admin = api.MapGroup("/admin/lookups").RequireAuthorization("SystemAdministrator");

        admin.MapGet("", async (ZevDbContext database, CancellationToken cancellationToken) =>
        {
            var types = await database.LookupTypes.AsNoTracking().OrderBy(item => item.Name)
                .Select(item => new
                {
                    item.Code, item.Name, item.Description, item.IsSystem,
                    items = item.Items.OrderBy(entry => entry.SortOrder).ThenBy(entry => entry.Name)
                        .Select(entry => new { entry.Id, entry.Code, entry.Name, entry.SortOrder, entry.IsActive,
                            entry.IsDefault, entry.ZevId, entry.UpdatedUtc }).ToList()
                }).ToListAsync(cancellationToken);
            return Results.Ok(types);
        });

        admin.MapPost("/{typeCode}/items", async (string typeCode, CreateLookupItemRequest request,
            TenantAccess access, LookupService lookups, HttpContext context, CancellationToken cancellationToken) =>
            Result(await lookups.CreateAsync(typeCode, request.Code, request.Name, request.SortOrder, request.IsDefault == true,
                request.ZevId, access.CurrentUserId, context.TraceIdentifier, cancellationToken), created: true));

        admin.MapPut("/items/{id:guid}", async (Guid id, UpdateLookupItemRequest request, TenantAccess access,
            LookupService lookups, HttpContext context, CancellationToken cancellationToken) =>
            Result(await lookups.UpdateAsync(id, request.Name, request.SortOrder, request.IsActive, request.IsDefault,
                access.CurrentUserId, context.TraceIdentifier, cancellationToken)));

        // Items keep the integrity of records that store their code: system list items are deactivated, not deleted.
        admin.MapDelete("/items/{id:guid}", async (Guid id, TenantAccess access, LookupService lookups,
            HttpContext context, CancellationToken cancellationToken) =>
        {
            var result = await lookups.DeleteAsync(id, access.CurrentUserId, context.TraceIdentifier, cancellationToken);
            return result.Status == LookupChangeStatus.Done ? Results.NoContent() : Result(result);
        });
        return api;
    }

    private static IResult Result(LookupChangeResult result, bool created = false) => result.Status switch
    {
        LookupChangeStatus.Done when created => Results.Created($"/api/admin/lookups/items/{result.Item!.Id}",
            LookupService.View(result.Item)),
        LookupChangeStatus.Done => Results.Ok(LookupService.View(result.Item!)),
        LookupChangeStatus.NotFound => Results.NotFound(),
        LookupChangeStatus.Invalid => Results.BadRequest(new { error = result.Error }),
        _ => Results.Conflict(new { error = result.Error }),
    };
}

public sealed record CreateLookupItemRequest(string? Code, string? Name, int? SortOrder, bool? IsDefault, Guid? ZevId);
public sealed record UpdateLookupItemRequest(string? Name, int SortOrder, bool IsActive, bool IsDefault);
