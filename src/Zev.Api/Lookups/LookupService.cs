using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Endpoints;

namespace Zev.Api.Lookups;

public enum LookupChangeStatus { Done, NotFound, Invalid, Conflict }
public sealed record LookupChangeResult(LookupChangeStatus Status, LookupItem? Item, string? Error = null);

// Writes to code lists. At most one default item per list and scope is also enforced by a filtered unique index,
// so the previous default is cleared and saved before the new one is set (SQL Server checks the index per statement).
public sealed class LookupService(ZevDbContext database)
{
    public async Task<LookupChangeResult> CreateAsync(string typeCode, string? code, string? name, int? sortOrder,
        bool isDefault, Guid? zevId, Guid? actorId, string? correlationId, CancellationToken cancellationToken)
    {
        if (!await database.LookupTypes.AnyAsync(item => item.Code == typeCode, cancellationToken))
            return new(LookupChangeStatus.NotFound, null);
        code = code?.Trim() ?? string.Empty;
        name = name?.Trim() ?? string.Empty;
        if (!AdminValidation.Code(code) || name.Length is 0 or > 200 || sortOrder is < 0 or > 100000)
            return new(LookupChangeStatus.Invalid, null,
                "Šifra (slova, brojevi, - i _, do 50 znakova) i naziv (do 200) su obavezni.");
        if (zevId is Guid zev && !await database.Zevs.AnyAsync(item => item.Id == zev, cancellationToken))
            return new(LookupChangeStatus.Invalid, null, "ZEV ne postoji.");
        await using var transaction = await BeginAsync(cancellationToken);
        var scope = database.LookupItems.Where(item => item.LookupTypeCode == typeCode && item.ZevId == zevId);
        if (await scope.AnyAsync(item => item.Code == code, cancellationToken))
            return new(LookupChangeStatus.Conflict, null, "Stavka s tom šifrom već postoji u šifarniku.");
        var order = sortOrder ?? (await scope.MaxAsync(item => (int?)item.SortOrder, cancellationToken) ?? 0) + 10;
        if (isDefault) await ClearDefaultAsync(typeCode, zevId, null, cancellationToken);
        var item = new LookupItem { LookupTypeCode = typeCode, Code = code, Name = name, SortOrder = order,
            IsDefault = isDefault, ZevId = zevId, CreatedUtc = DateTimeOffset.UtcNow, UpdatedByUserId = actorId };
        database.LookupItems.Add(item);
        Audit(item.Id, "LookupItemCreated", null, item, actorId, correlationId);
        await database.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return new(LookupChangeStatus.Done, item);
    }

    public async Task<LookupChangeResult> UpdateAsync(Guid id, string? name, int sortOrder, bool isActive, bool isDefault,
        Guid? actorId, string? correlationId, CancellationToken cancellationToken)
    {
        name = name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 200 || sortOrder is < 0 or > 100000)
            return new(LookupChangeStatus.Invalid, null, "Naziv (do 200 znakova) je obavezan, redoslijed 0–100000.");
        if (isDefault && !isActive)
            return new(LookupChangeStatus.Invalid, null, "Neaktivna stavka ne može biti zadana vrijednost.");
        await using var transaction = await BeginAsync(cancellationToken);
        var item = await database.LookupItems.SingleOrDefaultAsync(entry => entry.Id == id, cancellationToken);
        if (item is null) return new(LookupChangeStatus.NotFound, null);
        var before = View(item);
        if (isDefault && !item.IsDefault) await ClearDefaultAsync(item.LookupTypeCode, item.ZevId, item.Id, cancellationToken);
        item.Name = name;
        item.SortOrder = sortOrder;
        item.IsActive = isActive;
        item.IsDefault = isDefault;
        item.UpdatedUtc = DateTimeOffset.UtcNow;
        item.UpdatedByUserId = actorId;
        Audit(item.Id, "LookupItemUpdated", before, item, actorId, correlationId);
        await database.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return new(LookupChangeStatus.Done, item);
    }

    // Only items of non-system lists can be deleted; system list items are deactivated instead.
    public async Task<LookupChangeResult> DeleteAsync(Guid id, Guid? actorId, string? correlationId,
        CancellationToken cancellationToken)
    {
        var item = await database.LookupItems.Include(entry => entry.LookupType)
            .SingleOrDefaultAsync(entry => entry.Id == id, cancellationToken);
        if (item is null) return new(LookupChangeStatus.NotFound, null);
        if (item.LookupType.IsSystem)
            return new(LookupChangeStatus.Conflict, item, "Stavke sistemskog šifarnika se ne brišu; deaktivirajte stavku.");
        Audit(item.Id, "LookupItemDeleted", View(item), null, actorId, correlationId);
        database.LookupItems.Remove(item);
        await database.SaveChangesAsync(cancellationToken);
        return new(LookupChangeStatus.Done, item);
    }

    public static object View(LookupItem item) => new
    {
        item.Id, item.LookupTypeCode, item.Code, item.Name, item.SortOrder, item.IsActive, item.IsDefault, item.ZevId,
        item.UpdatedUtc
    };

    private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction?> BeginAsync(CancellationToken cancellationToken) =>
        database.Database.IsRelational()
            ? await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;

    private async Task ClearDefaultAsync(string typeCode, Guid? zevId, Guid? except, CancellationToken cancellationToken)
    {
        var previous = await database.LookupItems.Where(entry => entry.LookupTypeCode == typeCode && entry.ZevId == zevId
            && entry.IsDefault && entry.Id != except).ToListAsync(cancellationToken);
        if (previous.Count == 0) return;
        foreach (var other in previous) other.IsDefault = false;
        await database.SaveChangesAsync(cancellationToken);
    }

    private void Audit(Guid itemId, string action, object? before, LookupItem? after, Guid? actorId, string? correlationId) =>
        database.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorId, ZevId = after?.ZevId, EntityType = nameof(LookupItem), EntityId = itemId.ToString(),
            Action = action, BeforeJson = before is null ? null : JsonSerializer.Serialize(before),
            AfterJson = after is null ? null : JsonSerializer.Serialize(View(after)), CorrelationId = correlationId
        });
}
