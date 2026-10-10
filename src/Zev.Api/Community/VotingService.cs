using System.Data;
using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;

namespace Zev.Api.Community;

public enum VoteResult { Recorded, BallotNotFound, NotOpen, NotEligible, AlreadyVoted }

public sealed class VotingService(ZevDbContext database)
{
    public async Task<bool> OpenAsync(Guid zevId, Guid ballotId, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var ballot = await database.Ballots.SingleOrDefaultAsync(item => item.Id == ballotId
            && item.ZevId == zevId && item.Status == BallotStatus.Draft, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (ballot is null || now < ballot.OpensUtc || now >= ballot.ClosesUtc) return false;
        var asOf = DateOnly.FromDateTime(now.UtcDateTime);
        var owners = await database.Ownerships.AsNoTracking().Where(item => item.ZevId == zevId
            && item.ValidFrom <= asOf && (item.ValidTo == null || item.ValidTo > asOf)
            && item.PropertyUnit.IsActive && item.Person.IsActive)
            .Select(item => new { item.PropertyUnitId, item.PersonId,
                item.PropertyUnit.AreaSquareMeters, item.PropertyUnit.Coefficient }).ToListAsync(cancellationToken);
        if (owners.Count == 0) return false;
        foreach (var owner in owners)
        {
            var weight = ballot.Mode == VotingMode.OneUnitOneVote ? 1m
                : ballot.WeightBasis == VotingWeightBasis.UnitArea ? owner.AreaSquareMeters : owner.Coefficient;
            if (weight <= 0m) return false;
            database.BallotEligibilities.Add(new BallotEligibility
            {
                OrganizationId = ballot.OrganizationId, ZevId = zevId, BallotId = ballotId,
                PropertyUnitId = owner.PropertyUnitId, PersonId = owner.PersonId, Weight = weight
            });
        }
        ballot.EligibleWeight = owners.GroupBy(item => item.PropertyUnitId).Sum(group => ballot.Mode == VotingMode.OneUnitOneVote
            ? 1m : ballot.WeightBasis == VotingWeightBasis.UnitArea ? group.First().AreaSquareMeters : group.First().Coefficient);
        ballot.Status = BallotStatus.Open;
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<bool> CastAsync(Guid zevId, Guid ballotId, Guid unitId, Guid userId,
        VoteChoice choice, CancellationToken cancellationToken) =>
        await CastVoteAsync(zevId, ballotId, unitId, userId, choice, cancellationToken) == VoteResult.Recorded;

    public async Task<VoteResult> CastVoteAsync(Guid zevId, Guid ballotId, Guid unitId, Guid userId,
        VoteChoice choice, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (database.Database.IsSqlServer())
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT [Id] FROM [BallotEligibilities] WITH (UPDLOCK, HOLDLOCK) WHERE [BallotId] = {ballotId} AND [PropertyUnitId] = {unitId}",
                cancellationToken);
        var ballot = await database.Ballots.AsNoTracking().SingleOrDefaultAsync(item => item.Id == ballotId
            && item.ZevId == zevId, cancellationToken);
        if (ballot is null) return VoteResult.BallotNotFound;
        var now = DateTimeOffset.UtcNow;
        if (ballot.Status != BallotStatus.Open || now < ballot.OpensUtc || now >= ballot.ClosesUtc)
            return VoteResult.NotOpen;
        var asOf = DateOnly.FromDateTime(now.UtcDateTime);
        var eligibility = await database.BallotEligibilities.AsNoTracking().SingleOrDefaultAsync(item => item.BallotId == ballotId
            && item.PropertyUnitId == unitId && database.People.Any(person => person.Id == item.PersonId
                && person.UserId == userId && person.IsActive
                && person.UserLinkStatus == UserPersonLinkStatus.Verified)
            && database.Ownerships.Any(ownership => ownership.ZevId == zevId
                && ownership.PropertyUnitId == unitId && ownership.PersonId == item.PersonId
                && ownership.ValidFrom <= asOf
                && (ownership.ValidTo == null || ownership.ValidTo > asOf)),
            cancellationToken);
        // Not the caller's eligible unit: indistinguishable from a unit that does not exist (404 at the API).
        if (eligibility is null) return VoteResult.NotEligible;
        if (await database.Votes.AnyAsync(item => item.BallotId == ballotId
            && item.PropertyUnitId == unitId, cancellationToken)) return VoteResult.AlreadyVoted;
        database.Votes.Add(new Vote
        {
            OrganizationId = ballot.OrganizationId, ZevId = zevId, BallotId = ballotId,
            PropertyUnitId = unitId, PersonId = eligibility.PersonId, CastByUserId = userId,
            Choice = choice, WeightSnapshot = eligibility.Weight
        });
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return VoteResult.Recorded;
        }
        // A concurrent vote for the same unit won (unique index / deadlock); anything else propagates.
        catch (Exception problem) when (DbUpdateErrors.IsConcurrentWriteConflict(problem)) { return VoteResult.AlreadyVoted; }
    }

    public async Task<VotingOutcome?> CloseAsync(Guid zevId, Guid ballotId, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var ballot = await database.Ballots.SingleOrDefaultAsync(item => item.Id == ballotId
            && item.ZevId == zevId && item.Status == BallotStatus.Open, cancellationToken);
        if (ballot is null || DateTimeOffset.UtcNow < ballot.ClosesUtc) return null;
        var votes = await database.Votes.AsNoTracking().Where(item => item.BallotId == ballotId)
            .ToListAsync(cancellationToken);
        var result = VotingCalculator.Calculate(ballot.EligibleWeight ?? 0m, votes, ballot.QuorumPercent,
            ballot.ApprovalPercent, ballot.ApprovalDenominator);
        ballot.CastWeight = result.CastWeight;
        ballot.YesWeight = result.YesWeight;
        ballot.NoWeight = result.NoWeight;
        ballot.AbstainWeight = result.AbstainWeight;
        ballot.QuorumMet = result.QuorumMet;
        ballot.Approved = result.Approved;
        ballot.ClosedUtc = DateTimeOffset.UtcNow;
        ballot.Status = BallotStatus.Closed;
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
