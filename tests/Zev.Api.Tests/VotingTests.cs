using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using Zev.Api.Community;
using Zev.Api.Data;
using Zev.Api.Domain;

namespace Zev.Api.Tests;

public sealed class VotingTests
{
    [Fact]
    public void QuorumAndMajorityRulesAreConfigurable()
    {
        var votes = new[]
        {
            new Vote { Choice = VoteChoice.Yes, WeightSnapshot = 4m },
            new Vote { Choice = VoteChoice.No, WeightSnapshot = 2m },
            new Vote { Choice = VoteChoice.Abstain, WeightSnapshot = 2m }
        };
        var eligible = VotingCalculator.Calculate(10m, votes, 75m, 60m, ApprovalDenominator.Eligible);
        var participating = VotingCalculator.Calculate(10m, votes, 75m, 60m, ApprovalDenominator.YesNo);
        Assert.True(eligible.QuorumMet);
        Assert.False(eligible.Approved);
        Assert.True(participating.Approved);
        Assert.Equal(8m, participating.CastWeight);
    }

    [Fact]
    public async Task OneUnitCanVoteOnlyOnceAndEligibilityIsSnapshotted()
    {
        var options = new DbContextOptionsBuilder<ZevDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;
        await using var database = new ZevDbContext(options, new HttpContextAccessor());
        var organization = new Organization { Code = "O", Name = "Organization" };
        var zev = new global::Zev.Api.Domain.Zev { OrganizationId = organization.Id,
            Organization = organization, Code = "Z", Name = "ZEV" };
        var building = new Building { OrganizationId = organization.Id, ZevId = zev.Id,
            Zev = zev, Code = "B", Name = "Building" };
        var entrance = new Entrance { OrganizationId = organization.Id, ZevId = zev.Id,
            BuildingId = building.Id, Building = building, Code = "E", Name = "Entrance" };
        var unit = new PropertyUnit { OrganizationId = organization.Id, ZevId = zev.Id,
            BuildingId = building.Id, EntranceId = entrance.Id, Entrance = entrance,
            Code = "1", AreaSquareMeters = 60m };
        var user = new AppUser { Email = "owner@example.test", NormalizedEmail = "OWNER@EXAMPLE.TEST" };
        var person = new Person { OrganizationId = organization.Id, Organization = organization,
            UserId = user.Id, User = user, UserLinkStatus = UserPersonLinkStatus.Verified,
            DisplayName = "Owner" };
        var ownership = new Ownership { OrganizationId = organization.Id, ZevId = zev.Id,
            PropertyUnitId = unit.Id, PropertyUnit = unit, PersonId = person.Id, Person = person,
            ValidFrom = new DateOnly(2025, 1, 1), Share = 1m };
        var ballot = new Ballot { OrganizationId = organization.Id, ZevId = zev.Id,
            CreatedByUserId = user.Id, Title = "Test", Question = "Approve?",
            Mode = VotingMode.OneUnitOneVote, Kind = BallotKind.Formal,
            OpensUtc = DateTimeOffset.UtcNow.AddMinutes(-1), ClosesUtc = DateTimeOffset.UtcNow.AddMinutes(1),
            QuorumPercent = 50m, ApprovalPercent = 50m };
        database.AddRange(organization, zev, building, entrance, unit, user, person, ownership, ballot);
        await database.SaveChangesAsync();

        var service = new VotingService(database);
        Assert.True(await service.OpenAsync(zev.Id, ballot.Id, CancellationToken.None));
        Assert.Single(database.BallotEligibilities);
        Assert.True(await service.CastAsync(zev.Id, ballot.Id, unit.Id, user.Id, VoteChoice.Yes, CancellationToken.None));
        Assert.False(await service.CastAsync(zev.Id, ballot.Id, unit.Id, user.Id, VoteChoice.No, CancellationToken.None));
        Assert.Single(database.Votes);
        Assert.Throws<InvalidOperationException>(() =>
        {
            database.Votes.Single().Choice = VoteChoice.No;
            database.SaveChanges();
        });
    }
}
