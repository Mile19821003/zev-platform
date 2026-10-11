using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Maintenance;

namespace Zev.Api.RecycleBin;

public enum SoftDeleteOutcome { Done, NotFound, InUse, ParentDeleted, Conflict }

public sealed record SoftDeleteResult(SoftDeleteOutcome Outcome, string? Message = null)
{
    public IResult ToHttp() => Outcome switch
    {
        SoftDeleteOutcome.Done => Results.NoContent(),
        SoftDeleteOutcome.NotFound => Results.NotFound(),
        _ => Results.Conflict(new { error = Message })
    };
}

// Module and form shown in "Obrisani redovi" for each soft-deletable entity type.
public sealed record SoftDeleteType(string EntityType, string Module, string Form, Type ClrType);

public sealed record DeletedRecordView(long? Id, string EntityType, string Module, string Form, Guid RecordId,
    string Label, DateTimeOffset? DeletedAt, Guid? DeletedByUserId, string? DeletedBy);

public sealed class SoftDeleteAdminService(ZevDbContext database, MaintenanceScheduleService schedules)
{
    public static readonly IReadOnlyList<SoftDeleteType> Types =
    [
        new("Building", "Registar", "Zgrade", typeof(Building)),
        new("Entrance", "Registar", "Ulazi", typeof(Entrance)),
        new("PropertyUnit", "Registar", "Posebni dijelovi", typeof(PropertyUnit)),
        new("Person", "Registar", "Vlasnici / korisnici", typeof(Person)),
        new("Supplier", "Finansije", "Dobavljači", typeof(Supplier)),
        new("MaintenancePlan", "Održavanje", "Planovi održavanja", typeof(MaintenancePlan)),
    ];

    // Readable names of the data that blocks a delete; other types fall back to the class name.
    private static readonly Dictionary<string, string> DependentNames = new()
    {
        ["Entrance"] = "ulazi", ["PropertyUnit"] = "posebni dijelovi", ["Ownership"] = "vlasništva",
        ["Occupancy"] = "korisnici prostora", ["Charge"] = "zaduženja", ["Invoice"] = "računi",
        ["InvoiceLine"] = "stavke računa", ["Payment"] = "uplate", ["PaymentAllocation"] = "raspodjele uplata",
        ["Expense"] = "rashodi", ["MaintenanceTicket"] = "prijave kvarova", ["WorkOrder"] = "radni nalozi",
        ["Reminder"] = "opomene", ["LegalCase"] = "pravni predmeti", ["Announcement"] = "obavještenja",
        ["FinancialDocument"] = "finansijski dokumenti", ["PaymentAccount"] = "računi za plaćanje",
        ["ScheduledMaintenance"] = "termini održavanja",
    };

    // Delete only what nothing live points to; data still in use must be deactivated instead.
    public async Task<SoftDeleteResult> DeleteAsync<T>(T entity, Guid? actorId, CancellationToken cancellationToken)
        where T : class, ISoftDeletable
    {
        var users = new List<string>();
        foreach (var reference in References(database.Model, typeof(T)))
        {
            if (await AnyReferenceAsync(reference, entity.Id, cancellationToken))
                users.Add(DependentNames.GetValueOrDefault(reference.EntityType.ClrType.Name, reference.EntityType.ClrType.Name));
        }
        if (users.Count > 0)
            return new(SoftDeleteOutcome.InUse, $"Zapis se ne može obrisati jer ga koriste: {string.Join(", ", users.Distinct())}. "
                + "Deaktivirajte ga umjesto brisanja.");
        entity.IsDeleted = true;
        entity.DeletedAt = DateTimeOffset.UtcNow;
        entity.DeletedByUserId = actorId;
        try { await database.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return new(SoftDeleteOutcome.Conflict, "Zapis je u međuvremenu izmijenjen."); }
        return new(SoftDeleteOutcome.Done);
    }

    public async Task<SoftDeleteResult> RestoreAsync(string entityType, Guid recordId, CancellationToken cancellationToken)
    {
        ISoftDeletable? entity = entityType switch
        {
            "Building" => await Deleted(database.Buildings, recordId, cancellationToken),
            "Entrance" => await Deleted(database.Entrances, recordId, cancellationToken),
            "PropertyUnit" => await Deleted(database.PropertyUnits, recordId, cancellationToken),
            "Person" => await Deleted(database.People, recordId, cancellationToken),
            "Supplier" => await Deleted(database.Suppliers, recordId, cancellationToken),
            "MaintenancePlan" => await Deleted(database.MaintenancePlans, recordId, cancellationToken),
            _ => null
        };
        if (entity is null) return new(SoftDeleteOutcome.NotFound);
        if (await RestoreBlockedAsync(entity, cancellationToken) is { } blocked) return blocked;
        entity.IsDeleted = false;
        entity.DeletedAt = null;
        entity.DeletedByUserId = null;
        // A restored maintenance plan runs again: active, with its occurrences for the coming HorizonDays.
        if (entity is MaintenancePlan plan) plan.IsActive = true;
        try { await database.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return new(SoftDeleteOutcome.Conflict, "Zapis je u međuvremenu izmijenjen."); }
        catch (DbUpdateException) { return new(SoftDeleteOutcome.Conflict, "Postoji aktivan zapis s istim podacima."); }
        if (entity is MaintenancePlan restored)
            await schedules.EnsureUntilAsync(restored.ZevId, restored.Id,
                MaintenanceScheduleService.Today().AddDays(MaintenanceScheduleService.HorizonDays), cancellationToken);
        return new(SoftDeleteOutcome.Done);
    }

    // The parent must be live, and the code (or the user link) must not have been reused meanwhile.
    private async Task<SoftDeleteResult?> RestoreBlockedAsync(ISoftDeletable entity, CancellationToken cancellationToken)
    {
        switch (entity)
        {
            case Building building when await database.Buildings.AnyAsync(item => item.Id != building.Id
                && item.ZevId == building.ZevId && item.Code == building.Code, cancellationToken):
                return CodeTaken(building.Code);
            case Entrance entrance when !await database.Buildings.AnyAsync(item => item.Id == entrance.BuildingId, cancellationToken):
                return new(SoftDeleteOutcome.ParentDeleted, "Zgrada ovog ulaza je obrisana — najprije vratite zgradu.");
            case Entrance entrance when await database.Entrances.AnyAsync(item => item.Id != entrance.Id
                && item.BuildingId == entrance.BuildingId && item.Code == entrance.Code, cancellationToken):
                return CodeTaken(entrance.Code);
            case PropertyUnit unit when !await database.Entrances.AnyAsync(item => item.Id == unit.EntranceId, cancellationToken):
                return new(SoftDeleteOutcome.ParentDeleted, "Ulaz ovog posebnog dijela je obrisan — najprije vratite ulaz.");
            case PropertyUnit unit when await database.PropertyUnits.AnyAsync(item => item.Id != unit.Id
                && item.EntranceId == unit.EntranceId && item.Code == unit.Code, cancellationToken):
                return CodeTaken(unit.Code);
            case Person { UserId: Guid userId } person when await database.People.AnyAsync(item => item.Id != person.Id
                && item.OrganizationId == person.OrganizationId && item.UserId == userId, cancellationToken):
                return new(SoftDeleteOutcome.Conflict, "Korisnički nalog ove osobe je u međuvremenu povezan s drugom osobom.");
            default:
                return null;
        }
    }

    private static SoftDeleteResult CodeTaken(string code) =>
        new(SoftDeleteOutcome.Conflict, $"Šifra „{code}“ je u međuvremenu dodijeljena drugom zapisu.");

    private static Task<T?> Deleted<T>(DbSet<T> set, Guid id, CancellationToken cancellationToken) where T : class, ISoftDeletable =>
        set.IgnoreQueryFilters().SingleOrDefaultAsync(item => item.Id == id && item.IsDeleted, cancellationToken);

    public async Task<IReadOnlyList<DeletedRecordView>> ListAsync(string? module, string? entityType,
        DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var selected = Types.Where(type => (module is null || type.Module == module)
            && (entityType is null || type.EntityType == entityType)).Select(type => type.EntityType).ToHashSet();
        DateTimeOffset? fromUtc = from is DateOnly start ? new DateTimeOffset(start.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null;
        DateTimeOffset? toUtc = to is DateOnly end ? new DateTimeOffset(end.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null;
        var rows = new List<(string Type, DeletedRow Row)>();
        async Task Load<T>(string type, IQueryable<T> set, Expression<Func<T, DeletedRow>> projection) where T : class, ISoftDeletable
        {
            if (!selected.Contains(type)) return;
            var query = set.IgnoreQueryFilters().AsNoTracking().Where(item => item.IsDeleted);
            if (fromUtc is { } lower) query = query.Where(item => item.DeletedAt >= lower);
            if (toUtc is { } upper) query = query.Where(item => item.DeletedAt < upper);
            rows.AddRange((await query.Select(projection).ToListAsync(cancellationToken)).Select(row => (type, row)));
        }
        await Load("Building", database.Buildings, item => new DeletedRow(item.Id, item.Code + " · " + item.Name, item.DeletedAt, item.DeletedByUserId));
        await Load("Entrance", database.Entrances, item => new DeletedRow(item.Id, item.Code + " · " + item.Name, item.DeletedAt, item.DeletedByUserId));
        await Load("PropertyUnit", database.PropertyUnits, item => new DeletedRow(item.Id, item.Code, item.DeletedAt, item.DeletedByUserId));
        await Load("Person", database.People, item => new DeletedRow(item.Id, item.DisplayName, item.DeletedAt, item.DeletedByUserId));
        await Load("Supplier", database.Suppliers, item => new DeletedRow(item.Id, item.Name, item.DeletedAt, item.DeletedByUserId));
        await Load("MaintenancePlan", database.MaintenancePlans, item => new DeletedRow(item.Id, item.Title, item.DeletedAt, item.DeletedByUserId));

        var userIds = rows.Select(item => item.Row.DeletedByUserId).OfType<Guid>().Distinct().ToList();
        var emails = await database.Users.AsNoTracking().Where(item => userIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => item.Email, cancellationToken);
        // ID shown in the list: the audit entry of the (latest) delete.
        var recordIds = rows.Select(item => item.Row.Id.ToString()).ToList();
        var auditIds = (await database.AuditLogs.AsNoTracking()
                .Where(item => item.Action == "RecordDeleted" && recordIds.Contains(item.EntityId))
                .Select(item => new { item.EntityType, item.EntityId, item.Id }).ToListAsync(cancellationToken))
            .GroupBy(item => (item.EntityType, item.EntityId)).ToDictionary(group => group.Key, group => group.Max(item => item.Id));
        return rows.Select(item =>
            {
                var type = Types.Single(entry => entry.EntityType == item.Type);
                return new DeletedRecordView(auditIds.TryGetValue((item.Type, item.Row.Id.ToString()), out var auditId) ? auditId : null,
                    type.EntityType, type.Module, type.Form, item.Row.Id, item.Row.Label, item.Row.DeletedAt,
                    item.Row.DeletedByUserId,
                    item.Row.DeletedByUserId is Guid userId && emails.TryGetValue(userId, out var email) ? email : null);
            })
            .OrderByDescending(item => item.DeletedAt).ThenBy(item => item.Form).ToList();
    }

    private sealed record DeletedRow(Guid Id, string Label, DateTimeOffset? DeletedAt, Guid? DeletedByUserId);

    // Every column that points to a record of the given type: foreign keys from the model, plus Guid columns named
    // after the type without a mapped relationship (e.g. PreviousPersonId), so a new reference is covered automatically.
    public sealed record Reference(IEntityType EntityType, string Property);
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<Reference>> ReferenceCache = new();

    public static IReadOnlyList<Reference> References(IModel model, Type principal) => ReferenceCache.GetOrAdd(principal, _ =>
    {
        var references = new List<Reference>();
        foreach (var entityType in model.GetEntityTypes().Where(item => !item.IsOwned() && !item.HasSharedClrType
            && item.FindPrimaryKey() is not null && item.ClrType != principal))
        {
            foreach (var foreignKey in entityType.GetDeclaredForeignKeys().Where(key => key.PrincipalEntityType.ClrType == principal))
            {
                var index = foreignKey.PrincipalKey.Properties.Select(property => property.Name).ToList().IndexOf("Id");
                if (index >= 0) references.Add(new(entityType, foreignKey.Properties[index].Name));
            }
            foreach (var property in entityType.GetDeclaredProperties().Where(property => property.Name.EndsWith(principal.Name + "Id")
                && (property.ClrType == typeof(Guid) || property.ClrType == typeof(Guid?))))
            {
                references.Add(new(entityType, property.Name));
            }
        }
        return references.DistinctBy(item => (item.EntityType.Name, item.Property)).ToList();
    });

    private static readonly MethodInfo AnyReferenceMethod = typeof(SoftDeleteAdminService)
        .GetMethod(nameof(AnyReferenceOfAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    private Task<bool> AnyReferenceAsync(Reference reference, Guid id, CancellationToken cancellationToken) =>
        (Task<bool>)AnyReferenceMethod.MakeGenericMethod(reference.EntityType.ClrType)
            .Invoke(null, [database, reference.Property, id, cancellationToken])!;

    // Deleted dependents of a soft-deletable type are skipped by its query filter.
    private static Task<bool> AnyReferenceOfAsync<TDependent>(ZevDbContext database, string property, Guid id,
        CancellationToken cancellationToken) where TDependent : class =>
        database.Set<TDependent>().AnyAsync(item => EF.Property<Guid?>(item, property) == id, cancellationToken);
}
