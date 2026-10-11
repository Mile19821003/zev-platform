using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.RecycleBin;
using Zev.Api.Security;

namespace Zev.Api.Endpoints;

public sealed record RestoreRecordRequest(string? EntityType, Guid RecordId);

public static class RecycleBinEndpoints
{
    // Administration → "Obrisani redovi" (system administrators only).
    public static RouteGroupBuilder MapRecycleBinEndpoints(this RouteGroupBuilder api)
    {
        var admin = api.MapGroup("/admin/recycle-bin").RequireAuthorization("SystemAdministrator");

        admin.MapGet("", async (string? module, string? entityType, DateOnly? from, DateOnly? to, int? page, int? pageSize,
            SoftDeleteAdminService recycleBin, CancellationToken cancellationToken) =>
        {
            if (module is not null && SoftDeleteAdminService.Types.All(type => type.Module != module)
                || entityType is not null && SoftDeleteAdminService.Types.All(type => type.EntityType != entityType)
                || from > to) return Results.BadRequest();
            var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
            var rows = await recycleBin.ListAsync(module, entityType, from, to, cancellationToken);
            return Results.Ok(new
            {
                page = currentPage, pageSize = currentSize, totalCount = rows.Count,
                items = rows.Skip((currentPage - 1) * currentSize).Take(currentSize),
                types = SoftDeleteAdminService.Types.Select(type => new { type.EntityType, type.Module, type.Form })
            });
        });

        admin.MapPost("/restore", async (RestoreRecordRequest request, SoftDeleteAdminService recycleBin,
            CancellationToken cancellationToken) =>
        {
            if (SoftDeleteAdminService.Types.All(type => type.EntityType != request.EntityType)) return Results.BadRequest();
            return (await recycleBin.RestoreAsync(request.EntityType!, request.RecordId, cancellationToken)).ToHttp();
        });
        return api;
    }

    // "Obriši" in the manager workspace: the same edit permission that may deactivate the record.
    public static RouteGroupBuilder MapManagerDeleteEndpoints(this RouteGroupBuilder api)
    {
        api.MapDelete("/zevs/{zevId:guid}/registry/buildings/{buildingId:guid}", async (Guid zevId, Guid buildingId,
            TenantAccess access, ZevDbContext database, SoftDeleteAdminService recycleBin, CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Buildings.Edit", cancellationToken) is { } denied) return denied;
            var building = await database.Buildings.SingleOrDefaultAsync(item => item.Id == buildingId && item.ZevId == zevId, cancellationToken);
            return building is null ? Results.NotFound()
                : (await recycleBin.DeleteAsync(building, access.CurrentUserId, cancellationToken)).ToHttp();
        });
        api.MapDelete("/zevs/{zevId:guid}/registry/entrances/{entranceId:guid}", async (Guid zevId, Guid entranceId,
            TenantAccess access, ZevDbContext database, SoftDeleteAdminService recycleBin, CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Entrances.Edit", cancellationToken) is { } denied) return denied;
            var entrance = await database.Entrances.SingleOrDefaultAsync(item => item.Id == entranceId && item.ZevId == zevId, cancellationToken);
            return entrance is null ? Results.NotFound()
                : (await recycleBin.DeleteAsync(entrance, access.CurrentUserId, cancellationToken)).ToHttp();
        });
        api.MapDelete("/zevs/{zevId:guid}/registry/units/{unitId:guid}", async (Guid zevId, Guid unitId,
            TenantAccess access, ZevDbContext database, SoftDeleteAdminService recycleBin, CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "PropertyUnits.Edit", cancellationToken) is { } denied) return denied;
            var unit = await database.PropertyUnits.SingleOrDefaultAsync(item => item.Id == unitId && item.ZevId == zevId, cancellationToken);
            return unit is null ? Results.NotFound()
                : (await recycleBin.DeleteAsync(unit, access.CurrentUserId, cancellationToken)).ToHttp();
        });
        api.MapDelete("/zevs/{zevId:guid}/expenses/suppliers/{supplierId:guid}", async (Guid zevId, Guid supplierId,
            TenantAccess access, ZevDbContext database, SoftDeleteAdminService recycleBin, CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Expenses.Create", cancellationToken) is { } denied) return denied;
            var supplier = await database.Suppliers.SingleOrDefaultAsync(item => item.Id == supplierId
                && database.Zevs.Any(zev => zev.Id == zevId && zev.OrganizationId == item.OrganizationId), cancellationToken);
            return supplier is null ? Results.NotFound()
                : (await recycleBin.DeleteAsync(supplier, access.CurrentUserId, cancellationToken)).ToHttp();
        });
        return api;
    }

    // "Obriši" on the system administration registry screens (the /system group requires SystemAdministrator).
    public static RouteGroupBuilder MapSystemDeleteEndpoints(this RouteGroupBuilder system)
    {
        system.MapDelete("/buildings/{id:guid}", (Guid id, TenantAccess access, ZevDbContext database,
            SoftDeleteAdminService recycleBin, CancellationToken cancellationToken) =>
            DeleteAsync(database.Buildings, id, access, recycleBin, cancellationToken));
        system.MapDelete("/entrances/{id:guid}", (Guid id, TenantAccess access, ZevDbContext database,
            SoftDeleteAdminService recycleBin, CancellationToken cancellationToken) =>
            DeleteAsync(database.Entrances, id, access, recycleBin, cancellationToken));
        system.MapDelete("/property-units/{id:guid}", (Guid id, TenantAccess access, ZevDbContext database,
            SoftDeleteAdminService recycleBin, CancellationToken cancellationToken) =>
            DeleteAsync(database.PropertyUnits, id, access, recycleBin, cancellationToken));
        system.MapDelete("/people/{id:guid}", (Guid id, TenantAccess access, ZevDbContext database,
            SoftDeleteAdminService recycleBin, CancellationToken cancellationToken) =>
            DeleteAsync(database.People, id, access, recycleBin, cancellationToken));
        return system;
    }

    private static async Task<IResult> DeleteAsync<T>(DbSet<T> set, Guid id, TenantAccess access,
        SoftDeleteAdminService recycleBin, CancellationToken cancellationToken) where T : class, ISoftDeletable
    {
        var entity = await set.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return entity is null ? Results.NotFound() : (await recycleBin.DeleteAsync(entity, access.CurrentUserId, cancellationToken)).ToHttp();
    }
}
