using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Registry;
using Zev.Api.Security;

namespace Zev.Api.Endpoints;

public static class RegistryAdminEndpoints
{
    public static RouteGroupBuilder MapRegistryAdminEndpoints(this RouteGroupBuilder group)
    {
        MapUnits(group);
        MapPeople(group);
        MapOwnerships(group);
        MapOccupancies(group);
        return group;
    }

    private static void MapUnits(RouteGroupBuilder group)
    {
        group.MapGet("/property-units", async (int? page, int? pageSize, Guid? zevId, Guid? entranceId,
            string? search, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
            var query = database.PropertyUnits.AsNoTracking().Include(item => item.Entrance).AsQueryable();
            if (zevId is Guid selectedZev) query = query.Where(item => item.ZevId == selectedZev);
            if (entranceId is Guid selectedEntrance) query = query.Where(item => item.EntranceId == selectedEntrance);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(item => item.Code.Contains(term));
            }

            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query.OrderBy(item => item.Code).ThenBy(item => item.Id)
                .Skip((currentPage - 1) * currentSize).Take(currentSize).ToListAsync(cancellationToken);
            return Results.Ok(new { page = currentPage, pageSize = currentSize, totalCount, items = items.Select(UnitView) });
        });

        group.MapGet("/property-units/{unitId:guid}", async (Guid unitId, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            var unit = await database.PropertyUnits.AsNoTracking().Include(item => item.Entrance)
                .SingleOrDefaultAsync(item => item.Id == unitId, cancellationToken);
            return unit is null ? Results.NotFound() : Results.Ok(UnitView(unit));
        });

        group.MapPost("/property-units", async (CreateUnitRequest request, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!ValidUnit(request.Code, request.Type, request.AreaSquareMeters, request.Coefficient, request.RegisteredMembers, out var type))
                return Results.BadRequest(new { error = "Invalid property unit fields." });
            var entrance = await database.Entrances.Include(item => item.Building).ThenInclude(item => item.Zev)
                .ThenInclude(item => item.Organization).SingleOrDefaultAsync(item => item.Id == request.EntranceId, cancellationToken);
            if (entrance is null || !entrance.IsActive || !entrance.Building.IsActive
                || !entrance.Building.Zev.IsActive || !entrance.Building.Zev.Organization.IsActive) return Results.NotFound();
            var code = request.Code.Trim().ToUpperInvariant();
            if (await database.PropertyUnits.AnyAsync(item => item.EntranceId == entrance.Id && item.Code == code, cancellationToken))
                return Results.Conflict();
            var unit = new PropertyUnit
            {
                OrganizationId = entrance.OrganizationId, ZevId = entrance.ZevId,
                BuildingId = entrance.BuildingId, EntranceId = entrance.Id, Entrance = entrance,
                Code = code, Type = type, Floor = request.Floor,
                AreaSquareMeters = request.AreaSquareMeters, Coefficient = request.Coefficient,
                RegisteredMembers = request.RegisteredMembers
            };
            database.PropertyUnits.Add(unit);
            await database.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/system/property-units/{unit.Id}", UnitView(unit));
        });

        group.MapPut("/property-units/{unitId:guid}", async (Guid unitId, UpdateUnitRequest request,
            ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!ValidUnit(request.Code, request.Type, request.AreaSquareMeters, request.Coefficient, request.RegisteredMembers, out var type)
                || !AdminValidation.TryRowVersion(request.RowVersion, out var rowVersion)) return Results.BadRequest();
            var unit = await database.PropertyUnits.Include(item => item.Entrance)
                .SingleOrDefaultAsync(item => item.Id == unitId, cancellationToken);
            if (unit is null) return Results.NotFound();
            var code = request.Code.Trim().ToUpperInvariant();
            if (await database.PropertyUnits.AnyAsync(item => item.Id != unitId && item.EntranceId == unit.EntranceId
                && item.Code == code, cancellationToken)) return Results.Conflict();
            database.Entry(unit).Property(item => item.RowVersion).OriginalValue = rowVersion;
            unit.Code = code;
            unit.Type = type;
            unit.Floor = request.Floor;
            unit.AreaSquareMeters = request.AreaSquareMeters;
            unit.Coefficient = request.Coefficient;
            unit.RegisteredMembers = request.RegisteredMembers;
            try
            {
                await database.SaveChangesAsync(cancellationToken);
                return Results.Ok(UnitView(unit));
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new { error = "Property unit was changed by another request." });
            }
        });

        group.MapPatch("/property-units/{unitId:guid}/status", async (Guid unitId, UpdateStatusRequest request,
            ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!AdminValidation.TryRowVersion(request.RowVersion, out var rowVersion)) return Results.BadRequest();
            var unit = await database.PropertyUnits.Include(item => item.Entrance)
                .SingleOrDefaultAsync(item => item.Id == unitId, cancellationToken);
            if (unit is null) return Results.NotFound();
            database.Entry(unit).Property(item => item.RowVersion).OriginalValue = rowVersion;
            unit.IsActive = request.IsActive;
            try
            {
                await database.SaveChangesAsync(cancellationToken);
                return Results.Ok(UnitView(unit));
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new { error = "Property unit was changed by another request." });
            }
        });
    }

    private static void MapPeople(RouteGroupBuilder group)
    {
        group.MapGet("/people", async (int? page, int? pageSize, Guid? organizationId, string? search,
            ZevDbContext database, CancellationToken cancellationToken) =>
        {
            var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
            var query = database.People.AsNoTracking().AsQueryable();
            if (organizationId is Guid selectedOrganization) query = query.Where(item => item.OrganizationId == selectedOrganization);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(item => item.DisplayName.Contains(term) || (item.Email != null && item.Email.Contains(term)));
            }

            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query.OrderBy(item => item.DisplayName).ThenBy(item => item.Id)
                .Skip((currentPage - 1) * currentSize).Take(currentSize).ToListAsync(cancellationToken);
            return Results.Ok(new { page = currentPage, pageSize = currentSize, totalCount, items = items.Select(PersonView) });
        });

        group.MapGet("/people/{personId:guid}", async (Guid personId, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            var person = await database.People.AsNoTracking().SingleOrDefaultAsync(item => item.Id == personId, cancellationToken);
            return person is null ? Results.NotFound() : Results.Ok(PersonView(person));
        });

        group.MapPost("/people", async (CreatePersonRequest request, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!ValidPerson(request.Kind, request.DisplayName, request.TaxIdentifier, request.AddressLine,
                request.CityMunicipality, request.Email, request.Phone, out var kind)) return Results.BadRequest();
            if (!await database.Organizations.AnyAsync(item => item.Id == request.OrganizationId && item.IsActive, cancellationToken)
                || (request.UserId is Guid userId && !await database.Users.AnyAsync(item => item.Id == userId && item.IsActive, cancellationToken)))
                return Results.NotFound();
            if (request.UserId is Guid linkedUser && await database.People.AnyAsync(item => item.OrganizationId == request.OrganizationId
                && item.UserId == linkedUser, cancellationToken)) return Results.Conflict();
            var person = new Person
            {
                OrganizationId = request.OrganizationId, UserId = request.UserId, Kind = kind,
                DisplayName = request.DisplayName.Trim(), TaxIdentifier = AdminValidation.Clean(request.TaxIdentifier),
                AddressLine = AdminValidation.Clean(request.AddressLine), CityMunicipality = AdminValidation.Clean(request.CityMunicipality),
                Email = AdminValidation.Clean(request.Email), Phone = AdminValidation.Clean(request.Phone)
            };
            database.People.Add(person);
            await database.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/system/people/{person.Id}", PersonView(person));
        });

        group.MapPut("/people/{personId:guid}", async (Guid personId, UpdatePersonRequest request,
            ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!ValidPerson(request.Kind, request.DisplayName, request.TaxIdentifier, request.AddressLine,
                request.CityMunicipality, request.Email, request.Phone, out var kind)
                || !AdminValidation.TryRowVersion(request.RowVersion, out var rowVersion)) return Results.BadRequest();
            var person = await database.People.SingleOrDefaultAsync(item => item.Id == personId, cancellationToken);
            if (person is null) return Results.NotFound();
            if (request.UserId is Guid userId && !await database.Users.AnyAsync(item => item.Id == userId && item.IsActive, cancellationToken))
                return Results.NotFound();
            if (request.UserId is Guid linkedUser && await database.People.AnyAsync(item => item.Id != personId
                && item.OrganizationId == person.OrganizationId && item.UserId == linkedUser, cancellationToken)) return Results.Conflict();
            database.Entry(person).Property(item => item.RowVersion).OriginalValue = rowVersion;
            person.Kind = kind;
            person.DisplayName = request.DisplayName.Trim();
            if (person.UserId != request.UserId)
            {
                person.UserLinkStatus = UserPersonLinkStatus.Pending;
                person.UserLinkReviewedByUserId = null;
                person.UserLinkReviewedUtc = null;
            }
            person.UserId = request.UserId;
            person.TaxIdentifier = AdminValidation.Clean(request.TaxIdentifier);
            person.AddressLine = AdminValidation.Clean(request.AddressLine);
            person.CityMunicipality = AdminValidation.Clean(request.CityMunicipality);
            person.Email = AdminValidation.Clean(request.Email);
            person.Phone = AdminValidation.Clean(request.Phone);
            try
            {
                await database.SaveChangesAsync(cancellationToken);
                return Results.Ok(PersonView(person));
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new { error = "Person was changed by another request." });
            }
        });

        group.MapPost("/people/{personId:guid}/user-link/status", async (Guid personId,
            UpdateUserPersonLinkRequest request, TenantAccess access, ZevDbContext database,
            CancellationToken cancellationToken) =>
        {
            if (access.CurrentUserId is not Guid actorId
                || !Enum.TryParse<UserPersonLinkStatus>(request.Status, true, out var status)
                || !Enum.IsDefined(status) || status == UserPersonLinkStatus.Pending
                || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500
                || !AdminValidation.TryRowVersion(request.RowVersion, out var version))
                return Results.BadRequest();
            var person = await database.People.SingleOrDefaultAsync(item => item.Id == personId, cancellationToken);
            if (person is null || person.UserId is null || !person.IsActive) return Results.NotFound();
            if (status == UserPersonLinkStatus.Verified && !await database.Users.AnyAsync(item => item.Id == person.UserId
                && item.IsActive, cancellationToken)) return Results.Conflict();
            if (person.UserLinkStatus == status) return Results.Conflict();
            database.Entry(person).Property(item => item.RowVersion).OriginalValue = version;
            var prior = person.UserLinkStatus;
            person.UserLinkStatus = status;
            person.UserLinkReviewedByUserId = actorId;
            person.UserLinkReviewedUtc = DateTimeOffset.UtcNow;
            database.AuditLogs.Add(new AuditLog { ActorUserId = actorId, OrganizationId = person.OrganizationId,
                EntityType = nameof(Person), EntityId = person.Id.ToString(), Action = "UserLink",
                BeforeJson = System.Text.Json.JsonSerializer.Serialize(new { person.UserId, Status = prior.ToString() }),
                AfterJson = System.Text.Json.JsonSerializer.Serialize(new { person.UserId, Status = status.ToString(),
                    Reason = request.Reason.Trim() }) });
            try { await database.SaveChangesAsync(cancellationToken); return Results.Ok(PersonView(person)); }
            catch (DbUpdateConcurrencyException) { return Results.Conflict(); }
        });

        group.MapPatch("/people/{personId:guid}/status", async (Guid personId,
            UpdatePersonStatusRequest request, TenantAccess access, ZevDbContext database,
            CancellationToken cancellationToken) =>
        {
            if (access.CurrentUserId is not Guid actorId || string.IsNullOrWhiteSpace(request.Reason)
                || request.Reason.Trim().Length > 500
                || !AdminValidation.TryRowVersion(request.RowVersion, out var version)) return Results.BadRequest();
            var person = await database.People.SingleOrDefaultAsync(item => item.Id == personId, cancellationToken);
            if (person is null) return Results.NotFound();
            database.Entry(person).Property(item => item.RowVersion).OriginalValue = version;
            var previous = person.IsActive;
            person.IsActive = request.IsActive;
            if (!request.IsActive)
            {
                person.UserLinkStatus = UserPersonLinkStatus.Revoked;
                person.UserLinkReviewedByUserId = actorId;
                person.UserLinkReviewedUtc = DateTimeOffset.UtcNow;
            }
            database.AuditLogs.Add(new AuditLog { ActorUserId = actorId, OrganizationId = person.OrganizationId,
                EntityType = nameof(Person), EntityId = person.Id.ToString(), Action = "Status",
                BeforeJson = System.Text.Json.JsonSerializer.Serialize(new { IsActive = previous }),
                AfterJson = System.Text.Json.JsonSerializer.Serialize(new { person.IsActive,
                    Reason = request.Reason.Trim() }) });
            try { await database.SaveChangesAsync(cancellationToken); return Results.Ok(PersonView(person)); }
            catch (DbUpdateConcurrencyException) { return Results.Conflict(); }
        });
    }

    private static void MapOwnerships(RouteGroupBuilder group)
    {
        group.MapGet("/property-units/{unitId:guid}/ownerships", async (Guid unitId, DateOnly? asOf,
            ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await database.PropertyUnits.AnyAsync(item => item.Id == unitId, cancellationToken)) return Results.NotFound();
            var query = database.Ownerships.AsNoTracking().Include(item => item.Person)
                .Where(item => item.PropertyUnitId == unitId);
            if (asOf is DateOnly date) query = query.Where(item => item.ValidFrom <= date && (item.ValidTo == null || item.ValidTo > date));
            var items = await query.OrderBy(item => item.ValidFrom).ThenBy(item => item.Person.DisplayName)
                .Take(500).ToListAsync(cancellationToken);
            return Results.Ok(items.Select(OwnershipView));
        });

        group.MapPost("/ownerships", async (CreateOwnershipRequest request, OwnershipService service, ZevDbContext database,
            CancellationToken cancellationToken) =>
        {
            var result = await service.AddAsync(request.PropertyUnitId, request.PersonId, request.ValidFrom,
                request.ValidTo, request.Share, cancellationToken);
            if (result.Value is null) return Failure(result.Failure);
            var ownership = await database.Ownerships.Include(item => item.Person)
                .SingleAsync(item => item.Id == result.Value.Id, cancellationToken);
            return Results.Created($"/api/system/property-units/{ownership.PropertyUnitId}/ownerships", OwnershipView(ownership));
        });

        group.MapPost("/ownerships/{ownershipId:guid}/close", async (Guid ownershipId, CloseOwnershipRequest request,
            OwnershipService service, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            var result = await service.CloseAsync(ownershipId, request.ValidTo, cancellationToken);
            if (result.Value is null) return Failure(result.Failure);
            var ownership = await database.Ownerships.Include(item => item.Person)
                .SingleAsync(item => item.Id == result.Value.Id, cancellationToken);
            return Results.Ok(OwnershipView(ownership));
        });
    }

    private static void MapOccupancies(RouteGroupBuilder group)
    {
        group.MapGet("/property-units/{unitId:guid}/occupancies", async (Guid unitId, ZevDbContext database,
            CancellationToken cancellationToken) =>
        {
            if (!await database.PropertyUnits.AnyAsync(item => item.Id == unitId, cancellationToken)) return Results.NotFound();
            var items = await database.Occupancies.AsNoTracking().Include(item => item.Person)
                .Where(item => item.PropertyUnitId == unitId).OrderBy(item => item.ValidFrom)
                .Take(500).ToListAsync(cancellationToken);
            return Results.Ok(items.Select(OccupancyView));
        });

        group.MapPost("/occupancies", async (CreateOccupancyRequest request, ZevDbContext database,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse<OccupantKind>(request.Kind, true, out var kind) || !Enum.IsDefined(kind)
                || request.ValidTo <= request.ValidFrom) return Results.BadRequest();
            var unit = await database.PropertyUnits.SingleOrDefaultAsync(item => item.Id == request.PropertyUnitId && item.IsActive, cancellationToken);
            if (unit is null || !await database.People.AnyAsync(item => item.Id == request.PersonId
                && item.OrganizationId == unit.OrganizationId && item.IsActive, cancellationToken)) return Results.NotFound();
            var overlap = await database.Occupancies.AnyAsync(item => item.PropertyUnitId == unit.Id
                && item.PersonId == request.PersonId && (item.ValidTo == null || item.ValidTo > request.ValidFrom)
                && (request.ValidTo == null || item.ValidFrom < request.ValidTo), cancellationToken);
            if (overlap) return Results.Conflict();
            var occupancy = new Occupancy
            {
                OrganizationId = unit.OrganizationId, ZevId = unit.ZevId, PropertyUnitId = unit.Id,
                PersonId = request.PersonId, Kind = kind, ValidFrom = request.ValidFrom, ValidTo = request.ValidTo
            };
            database.Occupancies.Add(occupancy);
            await database.SaveChangesAsync(cancellationToken);
            occupancy.Person = await database.People.SingleAsync(item => item.Id == occupancy.PersonId, cancellationToken);
            return Results.Created($"/api/system/property-units/{unit.Id}/occupancies", OccupancyView(occupancy));
        });
    }

    private static IResult Failure(RegistryFailure failure) => failure switch
    {
        RegistryFailure.Invalid => Results.BadRequest(new { error = "Invalid ownership dates or share." }),
        RegistryFailure.Missing => Results.NotFound(),
        _ => Results.Conflict(new { error = "Ownership intervals or shares conflict." })
    };

    private static bool ValidUnit(string? code, string? typeName, decimal area, decimal coefficient, int members,
        out PropertyUnitType type)
    {
        type = default;
        return AdminValidation.Code(code) && Enum.TryParse(typeName, true, out type)
            && Enum.IsDefined(type) && area > 0m && coefficient > 0m && members >= 0;
    }

    private static bool ValidPerson(string? kindName, string? name, string? taxIdentifier, string? address,
        string? city, string? email, string? phone, out PersonKind kind) =>
        Enum.TryParse(kindName, true, out kind) && Enum.IsDefined(kind)
        && AdminValidation.Required(name, 200) && AdminValidation.Optional(taxIdentifier, 32)
        && AdminValidation.Optional(address, 250) && AdminValidation.Optional(city, 120)
        && AdminValidation.Optional(email, 320) && AdminValidation.Optional(phone, 40);

    private static object UnitView(PropertyUnit item) => new
    {
        item.Id, item.OrganizationId, item.ZevId, item.BuildingId, item.EntranceId,
        entranceName = item.Entrance.Name, item.Code, type = item.Type.ToString(), item.Floor,
        item.AreaSquareMeters, item.Coefficient, item.RegisteredMembers, item.IsActive,
        rowVersion = Convert.ToBase64String(item.RowVersion)
    };

    private static object PersonView(Person item) => new
    {
        item.Id, item.OrganizationId, item.UserId, userLinkStatus = item.UserLinkStatus.ToString(),
        item.UserLinkReviewedByUserId, item.UserLinkReviewedUtc, kind = item.Kind.ToString(), item.DisplayName,
        item.TaxIdentifier, item.AddressLine, item.CityMunicipality, item.Email, item.Phone,
        item.IsActive, rowVersion = Convert.ToBase64String(item.RowVersion)
    };

    private static object OwnershipView(Ownership item) => new
    {
        item.Id, item.OrganizationId, item.ZevId, item.PropertyUnitId, item.PersonId,
        personName = item.Person.DisplayName, item.ValidFrom, item.ValidTo, item.Share,
        rowVersion = Convert.ToBase64String(item.RowVersion)
    };

    private static object OccupancyView(Occupancy item) => new
    {
        item.Id, item.OrganizationId, item.ZevId, item.PropertyUnitId, item.PersonId,
        personName = item.Person.DisplayName, kind = item.Kind.ToString(), item.ValidFrom, item.ValidTo,
        rowVersion = Convert.ToBase64String(item.RowVersion)
    };
}

public sealed record CreateUnitRequest(Guid EntranceId, string Code, string Type, int? Floor,
    decimal AreaSquareMeters, decimal Coefficient, int RegisteredMembers);
public sealed record UpdateUnitRequest(string Code, string Type, int? Floor, decimal AreaSquareMeters,
    decimal Coefficient, int RegisteredMembers, string RowVersion);
public sealed record CreatePersonRequest(Guid OrganizationId, Guid? UserId, string Kind, string DisplayName,
    string? TaxIdentifier, string? AddressLine, string? CityMunicipality, string? Email, string? Phone);
public sealed record UpdatePersonRequest(Guid? UserId, string Kind, string DisplayName, string? TaxIdentifier,
    string? AddressLine, string? CityMunicipality, string? Email, string? Phone, string RowVersion);
public sealed record CreateOwnershipRequest(Guid PropertyUnitId, Guid PersonId, DateOnly ValidFrom, DateOnly? ValidTo, decimal Share);
public sealed record CloseOwnershipRequest(DateOnly ValidTo);
public sealed record UpdateUserPersonLinkRequest(string Status, string Reason, string RowVersion);
public sealed record UpdatePersonStatusRequest(bool IsActive, string Reason, string RowVersion);
public sealed record CreateOccupancyRequest(Guid PropertyUnitId, Guid PersonId, string Kind, DateOnly ValidFrom, DateOnly? ValidTo);
