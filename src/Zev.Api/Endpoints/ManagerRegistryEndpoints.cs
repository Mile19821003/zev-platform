using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Security;

namespace Zev.Api.Endpoints;

public static class ManagerRegistryEndpoints
{
    public static RouteGroupBuilder MapManagerRegistryEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/zevs/{zevId:guid}/registry");
        group.MapManagerRegistryWriteEndpoints();
        group.MapGet("/units", async (Guid zevId, string? search, Guid? buildingId, Guid? entranceId,
            bool? isActive, int? page, int? pageSize,
            TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "PropertyUnits.View", cancellationToken)) return Results.NotFound();
            var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
            var query = database.PropertyUnits.AsNoTracking().Where(item => item.ZevId == zevId);
            if (buildingId is Guid building) query = query.Where(item => item.BuildingId == building);
            if (entranceId is Guid entrance) query = query.Where(item => item.EntranceId == entrance);
            if (isActive is bool active) query = query.Where(item => item.IsActive == active);
            if (!string.IsNullOrWhiteSpace(search)) query = query.Where(item => item.Code.Contains(search.Trim()));
            var totalCount = await query.CountAsync(cancellationToken);
            // Type is read as its stored nvarchar(30) column and formatted after materialization;
            // Type.ToString() inside the query becomes CAST(... AS nvarchar(max)) and inflates the sort memory grant.
            var rows = await query.OrderBy(item => item.Entrance.Building.Name).ThenBy(item => item.Code)
                .Skip((currentPage - 1) * currentSize).Take(currentSize)
                .Select(item => new { item.Id, item.ZevId, item.BuildingId, item.EntranceId, item.Code,
                    item.Type, item.Floor, item.AreaSquareMeters, item.Coefficient,
                    item.RegisteredMembers, item.IsActive, building = item.Entrance.Building.Name,
                    entrance = item.Entrance.Name, item.RowVersion }).ToListAsync(cancellationToken);
            var items = rows.Select(item => new { item.Id, item.ZevId, item.BuildingId, item.EntranceId, item.Code,
                type = item.Type.ToString(), item.Floor, item.AreaSquareMeters, item.Coefficient,
                item.RegisteredMembers, item.IsActive, item.building, item.entrance, entranceName = item.entrance,
                rowVersion = Convert.ToBase64String(item.RowVersion) });
            return Results.Ok(new { page = currentPage, pageSize = currentSize, totalCount, items });
        });
        group.MapGet("/people", async (Guid zevId, string? search, bool? isActive, int? page, int? pageSize,
            TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Persons.View", cancellationToken)) return Results.NotFound();
            var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
            var query = database.People.AsNoTracking().Where(item => database.Ownerships.Any(ownership =>
                ownership.ZevId == zevId && ownership.PersonId == item.Id)
                || database.Occupancies.Any(occupancy => occupancy.ZevId == zevId && occupancy.PersonId == item.Id));
            if (!string.IsNullOrWhiteSpace(search)) query = query.Where(item => item.DisplayName.Contains(search.Trim()));
            if (isActive is bool active) query = query.Where(item => item.IsActive == active);
            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query.OrderBy(item => item.DisplayName).ThenBy(item => item.Id)
                .Skip((currentPage - 1) * currentSize).Take(currentSize)
                .Select(item => new { item.Id, item.OrganizationId, item.UserId, item.DisplayName,
                    kind = item.Kind.ToString(), item.TaxIdentifier, item.AddressLine,
                    item.CityMunicipality, item.Email, item.Phone, item.IsActive,
                    rowVersion = Convert.ToBase64String(item.RowVersion) }).ToListAsync(cancellationToken);
            return Results.Ok(new { page = currentPage, pageSize = currentSize, totalCount, items });
        });
        group.MapGet("/units/{unitId:guid}/ownerships", async (Guid zevId, Guid unitId,
            TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Ownership.View", cancellationToken)
                || !await database.PropertyUnits.AnyAsync(item => item.ZevId == zevId && item.Id == unitId,
                    cancellationToken)) return Results.NotFound();
            var rows = await database.Ownerships.AsNoTracking().Where(item => item.ZevId == zevId
                    && item.PropertyUnitId == unitId).OrderBy(item => item.ValidFrom).Take(500)
                .Select(item => new { item.Id, item.PersonId, personName = item.Person.DisplayName,
                    item.ValidFrom, item.ValidTo, item.Share }).ToListAsync(cancellationToken);
            return Results.Ok(rows);
        });
        return api;
    }
}
