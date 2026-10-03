using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Registry;
using Zev.Api.Security;

namespace Zev.Api.Endpoints;

public static class ManagerRegistryWriteEndpoints
{
    public static RouteGroupBuilder MapManagerRegistryWriteEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/buildings", async (Guid zevId, string? search, bool? isActive, int? page, int? pageSize,
            TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Registry.View", cancellationToken)) return Results.NotFound();
            var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
            var query = database.Buildings.AsNoTracking().Where(item => item.ZevId == zevId);
            if (!string.IsNullOrWhiteSpace(search)) query = query.Where(item => item.Name.Contains(search.Trim())
                || item.Code.Contains(search.Trim()));
            if (isActive is bool active) query = query.Where(item => item.IsActive == active);
            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query.OrderBy(item => item.Name).ThenBy(item => item.Id)
                .Skip((currentPage - 1) * currentSize).Take(currentSize).ToListAsync(cancellationToken);
            return Results.Ok(new { page = currentPage, pageSize = currentSize, totalCount,
                items = items.Select(BuildingView) });
        });

        group.MapPost("/buildings", async (Guid zevId, ManagerBuildingRequest request, TenantAccess access,
            ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Buildings.Create", cancellationToken)) return Results.NotFound();
            if (!ValidBuilding(request.Code, request.Name, request.AddressLine)) return Results.BadRequest();
            var organizationId = await database.Zevs.Where(item => item.Id == zevId)
                .Select(item => item.OrganizationId).SingleAsync(cancellationToken);
            var building = new Building { ZevId = zevId, OrganizationId = organizationId,
                Code = request.Code.Trim().ToUpperInvariant(), Name = request.Name.Trim(),
                AddressLine = AdminValidation.Clean(request.AddressLine) };
            database.Buildings.Add(building);
            try { await database.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateException) { return Results.Conflict(); }
            return Results.Created($"/api/zevs/{zevId}/registry/buildings/{building.Id}", BuildingView(building));
        });

        group.MapPut("/buildings/{buildingId:guid}", async (Guid zevId, Guid buildingId,
            ManagerBuildingRequest request, TenantAccess access, ZevDbContext database,
            CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Buildings.Edit", cancellationToken)) return Results.NotFound();
            if (!ValidBuilding(request.Code, request.Name, request.AddressLine)
                || !AdminValidation.TryRowVersion(request.RowVersion, out var version)) return Results.BadRequest();
            var building = await database.Buildings.SingleOrDefaultAsync(item => item.Id == buildingId
                && item.ZevId == zevId, cancellationToken);
            if (building is null) return Results.NotFound();
            database.Entry(building).Property(item => item.RowVersion).OriginalValue = version;
            building.Code = request.Code.Trim().ToUpperInvariant();
            building.Name = request.Name.Trim();
            building.AddressLine = AdminValidation.Clean(request.AddressLine);
            try { await database.SaveChangesAsync(cancellationToken); return Results.Ok(BuildingView(building)); }
            catch (DbUpdateException) { return Results.Conflict(); }
        });

        group.MapPatch("/buildings/{buildingId:guid}/status", async (Guid zevId, Guid buildingId,
            UpdateStatusRequest request, TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Buildings.Edit", cancellationToken)) return Results.NotFound();
            if (!AdminValidation.TryRowVersion(request.RowVersion, out var version)) return Results.BadRequest();
            var building = await database.Buildings.SingleOrDefaultAsync(item => item.Id == buildingId
                && item.ZevId == zevId, cancellationToken);
            if (building is null) return Results.NotFound();
            database.Entry(building).Property(item => item.RowVersion).OriginalValue = version;
            building.IsActive = request.IsActive;
            try { await database.SaveChangesAsync(cancellationToken); return Results.Ok(BuildingView(building)); }
            catch (DbUpdateException) { return Results.Conflict(); }
        });

        group.MapGet("/entrances", async (Guid zevId, Guid? buildingId, string? search, bool? isActive,
            int? page, int? pageSize, TenantAccess access, ZevDbContext database,
            CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Registry.View", cancellationToken)) return Results.NotFound();
            var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
            var query = database.Entrances.AsNoTracking().Include(item => item.Building)
                .Where(item => item.ZevId == zevId);
            if (buildingId is Guid building) query = query.Where(item => item.BuildingId == building);
            if (!string.IsNullOrWhiteSpace(search)) query = query.Where(item => item.Name.Contains(search.Trim())
                || item.Code.Contains(search.Trim()));
            if (isActive is bool active) query = query.Where(item => item.IsActive == active);
            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query.OrderBy(item => item.Building.Name).ThenBy(item => item.Code)
                .Skip((currentPage - 1) * currentSize).Take(currentSize).ToListAsync(cancellationToken);
            return Results.Ok(new { page = currentPage, pageSize = currentSize, totalCount,
                items = items.Select(EntranceView) });
        });

        group.MapPost("/entrances", async (Guid zevId, CreateEntranceRequest request, TenantAccess access,
            ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Entrances.Create", cancellationToken)) return Results.NotFound();
            if (!AdminValidation.Code(request.Code) || !AdminValidation.Required(request.Name, 200)) return Results.BadRequest();
            var building = await database.Buildings.SingleOrDefaultAsync(item => item.Id == request.BuildingId
                && item.ZevId == zevId && item.IsActive, cancellationToken);
            if (building is null) return Results.NotFound();
            var entrance = new Entrance { OrganizationId = building.OrganizationId, ZevId = zevId,
                BuildingId = building.Id, Building = building, Code = request.Code.Trim().ToUpperInvariant(),
                Name = request.Name.Trim() };
            database.Entrances.Add(entrance);
            try { await database.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateException) { return Results.Conflict(); }
            return Results.Created($"/api/zevs/{zevId}/registry/entrances/{entrance.Id}", EntranceView(entrance));
        });

        group.MapPut("/entrances/{entranceId:guid}", async (Guid zevId, Guid entranceId,
            UpdateEntranceRequest request, TenantAccess access, ZevDbContext database,
            CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Entrances.Edit", cancellationToken)) return Results.NotFound();
            if (!AdminValidation.Code(request.Code) || !AdminValidation.Required(request.Name, 200)
                || !AdminValidation.TryRowVersion(request.RowVersion, out var version)) return Results.BadRequest();
            var entrance = await database.Entrances.Include(item => item.Building)
                .SingleOrDefaultAsync(item => item.Id == entranceId && item.ZevId == zevId, cancellationToken);
            if (entrance is null) return Results.NotFound();
            database.Entry(entrance).Property(item => item.RowVersion).OriginalValue = version;
            entrance.Code = request.Code.Trim().ToUpperInvariant();
            entrance.Name = request.Name.Trim();
            try { await database.SaveChangesAsync(cancellationToken); return Results.Ok(EntranceView(entrance)); }
            catch (DbUpdateException) { return Results.Conflict(); }
        });

        group.MapPatch("/entrances/{entranceId:guid}/status", async (Guid zevId, Guid entranceId,
            UpdateStatusRequest request, TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Entrances.Edit", cancellationToken)) return Results.NotFound();
            if (!AdminValidation.TryRowVersion(request.RowVersion, out var version)) return Results.BadRequest();
            var entrance = await database.Entrances.Include(item => item.Building)
                .SingleOrDefaultAsync(item => item.Id == entranceId && item.ZevId == zevId, cancellationToken);
            if (entrance is null) return Results.NotFound();
            if (request.IsActive && !entrance.Building.IsActive) return Results.Conflict();
            database.Entry(entrance).Property(item => item.RowVersion).OriginalValue = version;
            entrance.IsActive = request.IsActive;
            try { await database.SaveChangesAsync(cancellationToken); return Results.Ok(EntranceView(entrance)); }
            catch (DbUpdateException) { return Results.Conflict(); }
        });

        group.MapPost("/units", async (Guid zevId, CreateUnitRequest request, TenantAccess access,
            ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "PropertyUnits.Create", cancellationToken)) return Results.NotFound();
            if (!ValidUnit(request.Code, request.Type, request.AreaSquareMeters, request.Coefficient,
                request.RegisteredMembers, out var type)) return Results.BadRequest();
            var entrance = await database.Entrances.Include(item => item.Building)
                .SingleOrDefaultAsync(item => item.Id == request.EntranceId && item.ZevId == zevId
                    && item.IsActive && item.Building.IsActive, cancellationToken);
            if (entrance is null) return Results.NotFound();
            var unit = new PropertyUnit { OrganizationId = entrance.OrganizationId, ZevId = zevId,
                BuildingId = entrance.BuildingId, EntranceId = entrance.Id, Entrance = entrance,
                Code = request.Code.Trim().ToUpperInvariant(), Type = type, Floor = request.Floor,
                AreaSquareMeters = request.AreaSquareMeters, Coefficient = request.Coefficient,
                RegisteredMembers = request.RegisteredMembers };
            database.PropertyUnits.Add(unit);
            try { await database.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateException) { return Results.Conflict(); }
            return Results.Created($"/api/zevs/{zevId}/registry/units/{unit.Id}", UnitView(unit));
        });

        group.MapPut("/units/{unitId:guid}", async (Guid zevId, Guid unitId, UpdateUnitRequest request,
            TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "PropertyUnits.Edit", cancellationToken)) return Results.NotFound();
            if (!ValidUnit(request.Code, request.Type, request.AreaSquareMeters, request.Coefficient,
                request.RegisteredMembers, out var type)
                || !AdminValidation.TryRowVersion(request.RowVersion, out var version)) return Results.BadRequest();
            var unit = await database.PropertyUnits.Include(item => item.Entrance)
                .SingleOrDefaultAsync(item => item.Id == unitId && item.ZevId == zevId, cancellationToken);
            if (unit is null) return Results.NotFound();
            database.Entry(unit).Property(item => item.RowVersion).OriginalValue = version;
            unit.Code = request.Code.Trim().ToUpperInvariant();
            unit.Type = type;
            unit.Floor = request.Floor;
            unit.AreaSquareMeters = request.AreaSquareMeters;
            unit.Coefficient = request.Coefficient;
            unit.RegisteredMembers = request.RegisteredMembers;
            try { await database.SaveChangesAsync(cancellationToken); return Results.Ok(UnitView(unit)); }
            catch (DbUpdateException) { return Results.Conflict(); }
        });

        group.MapPatch("/units/{unitId:guid}/status", async (Guid zevId, Guid unitId,
            UpdateStatusRequest request, TenantAccess access, ZevDbContext database,
            CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "PropertyUnits.Edit", cancellationToken)) return Results.NotFound();
            if (!AdminValidation.TryRowVersion(request.RowVersion, out var version)) return Results.BadRequest();
            var unit = await database.PropertyUnits.Include(item => item.Entrance)
                .SingleOrDefaultAsync(item => item.Id == unitId && item.ZevId == zevId, cancellationToken);
            if (unit is null) return Results.NotFound();
            if (request.IsActive && (!unit.Entrance.IsActive || !await database.Buildings.AnyAsync(item =>
                    item.Id == unit.BuildingId && item.IsActive, cancellationToken))) return Results.Conflict();
            database.Entry(unit).Property(item => item.RowVersion).OriginalValue = version;
            unit.IsActive = request.IsActive;
            try { await database.SaveChangesAsync(cancellationToken); return Results.Ok(UnitView(unit)); }
            catch (DbUpdateException) { return Results.Conflict(); }
        });

        group.MapPost("/people", async (Guid zevId, ManagerPersonRequest request, TenantAccess access,
            ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Persons.Create", cancellationToken)) return Results.NotFound();
            if (!ValidPerson(request, out var kind)) return Results.BadRequest();
            var organizationId = await database.Zevs.Where(item => item.Id == zevId)
                .Select(item => item.OrganizationId).SingleAsync(cancellationToken);
            var person = new Person { OrganizationId = organizationId, Kind = kind,
                DisplayName = request.DisplayName.Trim(), TaxIdentifier = AdminValidation.Clean(request.TaxIdentifier),
                AddressLine = AdminValidation.Clean(request.AddressLine),
                CityMunicipality = AdminValidation.Clean(request.CityMunicipality),
                Email = AdminValidation.Clean(request.Email), Phone = AdminValidation.Clean(request.Phone) };
            database.People.Add(person);
            await database.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/zevs/{zevId}/registry/people/{person.Id}", PersonView(person));
        });

        group.MapPut("/people/{personId:guid}", async (Guid zevId, Guid personId,
            ManagerPersonRequest request, TenantAccess access, ZevDbContext database,
            CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Persons.Edit", cancellationToken)) return Results.NotFound();
            if (!ValidPerson(request, out var kind)
                || !AdminValidation.TryRowVersion(request.RowVersion, out var version)) return Results.BadRequest();
            var person = await database.People.SingleOrDefaultAsync(item => item.Id == personId
                && (database.Ownerships.Any(ownership => ownership.PersonId == item.Id && ownership.ZevId == zevId)
                    || database.Occupancies.Any(occupancy => occupancy.PersonId == item.Id && occupancy.ZevId == zevId)),
                cancellationToken);
            if (person is null || await database.Ownerships.AnyAsync(item => item.PersonId == personId
                    && item.ZevId != zevId, cancellationToken)
                || await database.Occupancies.AnyAsync(item => item.PersonId == personId && item.ZevId != zevId,
                    cancellationToken)) return Results.NotFound();
            database.Entry(person).Property(item => item.RowVersion).OriginalValue = version;
            person.Kind = kind;
            person.DisplayName = request.DisplayName.Trim();
            person.TaxIdentifier = AdminValidation.Clean(request.TaxIdentifier);
            person.AddressLine = AdminValidation.Clean(request.AddressLine);
            person.CityMunicipality = AdminValidation.Clean(request.CityMunicipality);
            person.Email = AdminValidation.Clean(request.Email);
            person.Phone = AdminValidation.Clean(request.Phone);
            try { await database.SaveChangesAsync(cancellationToken); return Results.Ok(PersonView(person)); }
            catch (DbUpdateException) { return Results.Conflict(); }
        });

        group.MapPost("/ownerships", async (Guid zevId, CreateOwnershipRequest request, TenantAccess access,
            ZevDbContext database, OwnershipService service, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Ownership.Create", cancellationToken)
                || !await database.PropertyUnits.AnyAsync(item => item.Id == request.PropertyUnitId
                    && item.ZevId == zevId, cancellationToken)
                || await database.Ownerships.AnyAsync(item => item.PersonId == request.PersonId
                    && item.ZevId != zevId, cancellationToken)
                || await database.Occupancies.AnyAsync(item => item.PersonId == request.PersonId
                    && item.ZevId != zevId, cancellationToken)) return Results.NotFound();
            var result = await service.AddAsync(request.PropertyUnitId, request.PersonId, request.ValidFrom,
                request.ValidTo, request.Share, cancellationToken);
            return result.Value is not null
                ? Results.Created($"/api/zevs/{zevId}/registry/units/{request.PropertyUnitId}/ownerships",
                    new { result.Value.Id })
                : result.Failure == RegistryFailure.Missing ? Results.NotFound()
                : result.Failure == RegistryFailure.Invalid ? Results.BadRequest() : Results.Conflict();
        });

        group.MapPost("/ownerships/{ownershipId:guid}/close", async (Guid zevId, Guid ownershipId,
            CloseOwnershipRequest request, TenantAccess access, ZevDbContext database,
            OwnershipService service, CancellationToken cancellationToken) =>
        {
            if (!await access.HasPermissionAsync(zevId, "Ownership.Close", cancellationToken)
                || !await database.Ownerships.AnyAsync(item => item.Id == ownershipId && item.ZevId == zevId,
                    cancellationToken)) return Results.NotFound();
            var result = await service.CloseAsync(ownershipId, request.ValidTo, cancellationToken);
            return result.Value is not null ? Results.Ok(new { result.Value.Id, result.Value.ValidTo })
                : result.Failure == RegistryFailure.Invalid ? Results.BadRequest() : Results.Conflict();
        });

        return group;
    }

    private static bool ValidBuilding(string? code, string? name, string? address) =>
        AdminValidation.Code(code) && AdminValidation.Required(name, 200) && AdminValidation.Optional(address, 250);

    private static bool ValidUnit(string? code, string? typeName, decimal area, decimal coefficient, int members,
        out PropertyUnitType type)
    {
        type = default;
        return AdminValidation.Code(code) && Enum.TryParse(typeName, true, out type)
            && Enum.IsDefined(type) && area > 0m && coefficient > 0m && members >= 0;
    }

    private static bool ValidPerson(ManagerPersonRequest request, out PersonKind kind) =>
        Enum.TryParse(request.Kind, true, out kind) && Enum.IsDefined(kind)
        && AdminValidation.Required(request.DisplayName, 200)
        && AdminValidation.Optional(request.TaxIdentifier, 32)
        && AdminValidation.Optional(request.AddressLine, 250)
        && AdminValidation.Optional(request.CityMunicipality, 120)
        && AdminValidation.Optional(request.Email, 320)
        && AdminValidation.Optional(request.Phone, 40);

    private static object BuildingView(Building item) => new { item.Id, item.ZevId, item.Code,
        item.Name, item.AddressLine, item.IsActive, rowVersion = Convert.ToBase64String(item.RowVersion) };
    private static object EntranceView(Entrance item) => new { item.Id, item.ZevId, item.BuildingId,
        buildingName = item.Building.Name, item.Code, item.Name, item.IsActive,
        rowVersion = Convert.ToBase64String(item.RowVersion) };
    private static object UnitView(PropertyUnit item) => new { item.Id, item.ZevId, item.BuildingId,
        item.EntranceId, entranceName = item.Entrance.Name, item.Code, type = item.Type.ToString(),
        item.Floor, item.AreaSquareMeters, item.Coefficient, item.RegisteredMembers, item.IsActive,
        rowVersion = Convert.ToBase64String(item.RowVersion) };
    private static object PersonView(Person item) => new { item.Id, item.OrganizationId, item.DisplayName,
        kind = item.Kind.ToString(), item.TaxIdentifier, item.AddressLine, item.CityMunicipality,
        item.Email, item.Phone, item.IsActive, rowVersion = Convert.ToBase64String(item.RowVersion) };
}

public sealed record ManagerBuildingRequest(string Code, string Name, string? AddressLine, string? RowVersion = null);
public sealed record ManagerPersonRequest(string Kind, string DisplayName, string? TaxIdentifier,
    string? AddressLine, string? CityMunicipality, string? Email, string? Phone, string? RowVersion = null);
