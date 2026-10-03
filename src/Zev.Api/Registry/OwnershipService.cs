using System.Data;
using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;

namespace Zev.Api.Registry;

public enum RegistryFailure { None, Invalid, Missing, Conflict }

public sealed record RegistryResult<T>(T? Value, RegistryFailure Failure) where T : class;

public sealed class OwnershipService(ZevDbContext database)
{
    public async Task<RegistryResult<Ownership>> AddAsync(Guid unitId, Guid personId, DateOnly validFrom,
        DateOnly? validTo, decimal share, CancellationToken cancellationToken)
    {
        if (share is <= 0m or > 1m || validTo <= validFrom)
        {
            return new(null, RegistryFailure.Invalid);
        }

        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (database.Database.IsSqlServer())
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT [Id] FROM [PropertyUnits] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {unitId}", cancellationToken);
        var unit = await database.PropertyUnits.SingleOrDefaultAsync(item => item.Id == unitId && item.IsActive, cancellationToken);
        if (unit is null || !await database.People.AnyAsync(item => item.Id == personId
            && item.OrganizationId == unit.OrganizationId && item.IsActive, cancellationToken))
        {
            return new(null, RegistryFailure.Missing);
        }

        var existing = await database.Ownerships.Where(item => item.PropertyUnitId == unitId).ToListAsync(cancellationToken);
        var ownership = new Ownership
        {
            OrganizationId = unit.OrganizationId, ZevId = unit.ZevId,
            PropertyUnitId = unit.Id, PersonId = personId,
            ValidFrom = validFrom, ValidTo = validTo, Share = share
        };
        if (!OwnershipTimeline.IsValid(existing.Append(ownership)))
        {
            return new(null, RegistryFailure.Conflict);
        }

        unit.OwnershipRevision++;
        database.Ownerships.Add(ownership);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(ownership, RegistryFailure.None);
        }
        catch (DbUpdateException)
        {
            return new(null, RegistryFailure.Conflict);
        }
    }

    public async Task<RegistryResult<Ownership>> CloseAsync(Guid ownershipId, DateOnly validTo,
        CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var ownership = await database.Ownerships.SingleOrDefaultAsync(item => item.Id == ownershipId, cancellationToken);
        if (ownership is null) return new(null, RegistryFailure.Missing);
        if (validTo <= ownership.ValidFrom || ownership.ValidTo is not null)
        {
            return new(null, RegistryFailure.Invalid);
        }

        var unit = await database.PropertyUnits.SingleAsync(item => item.Id == ownership.PropertyUnitId, cancellationToken);
        ownership.ValidTo = validTo;
        unit.OwnershipRevision++;
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(ownership, RegistryFailure.None);
        }
        catch (DbUpdateException)
        {
            return new(null, RegistryFailure.Conflict);
        }
    }
}
