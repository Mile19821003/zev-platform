using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Zev.Api.Community;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Registry;

namespace Zev.Api.Tests;

[Collection("Sql integration")]
public sealed class SqlOwnershipVoteConcurrencyTests
{
    [Fact]
    public async Task ConcurrentOwnershipAndIdenticalVotesKeepSingleWinner()
    {
        var connection = Environment.GetEnvironmentVariable("ZEV_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var builder = new SqlConnectionStringBuilder(connection)
        {
            InitialCatalog = $"ZevOwnerVoteRace_{Guid.NewGuid():N}"
        };
        var options = new DbContextOptionsBuilder<ZevDbContext>().UseSqlServer(builder.ConnectionString).Options;
        await using var database = new ZevDbContext(options, new HttpContextAccessor());
        try
        {
            await database.Database.MigrateAsync();
            var organization = new Organization { Code = "RACE", Name = "Race" };
            var zev = new global::Zev.Api.Domain.Zev { Organization = organization, Code = "RACE", Name = "Race" };
            var building = new Building { OrganizationId = organization.Id, Zev = zev, Code = "B", Name = "B" };
            var entrance = new Entrance { OrganizationId = organization.Id, ZevId = zev.Id,
                Building = building, Code = "E", Name = "E" };
            var unit = new PropertyUnit { OrganizationId = organization.Id, ZevId = zev.Id,
                BuildingId = building.Id, Entrance = entrance, Code = "1", AreaSquareMeters = 40m };
            var user = new AppUser { Email = "race@example.invalid", NormalizedEmail = "RACE@EXAMPLE.INVALID",
                PasswordHash = "test-only" };
            var firstPerson = new Person { Organization = organization, DisplayName = "First" };
            var secondPerson = new Person { Organization = organization, DisplayName = "Second" };
            database.AddRange(organization, zev, building, entrance, unit, user, firstPerson,
                secondPerson);
            await database.SaveChangesAsync();

            await using (var firstDatabase = new ZevDbContext(options, new HttpContextAccessor()))
            await using (var secondDatabase = new ZevDbContext(options, new HttpContextAccessor()))
            {
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                async Task<RegistryResult<Ownership>> AddAsync(ZevDbContext context, Guid personId)
                {
                    await start.Task;
                    return await new OwnershipService(context).AddAsync(unit.Id, personId,
                        new DateOnly(2026, 1, 1), null, 1m, CancellationToken.None);
                }
                var first = AddAsync(firstDatabase, firstPerson.Id);
                var second = AddAsync(secondDatabase, secondPerson.Id);
                start.SetResult();
                var results = await Task.WhenAll(first, second);
                Assert.Single(results, result => result.Failure == RegistryFailure.None);
                Assert.Single(results, result => result.Failure == RegistryFailure.Conflict);
                Assert.Single(await database.Ownerships.AsNoTracking().ToListAsync());
            }

            var ownerId = await database.Ownerships.AsNoTracking()
                .Where(item => item.PropertyUnitId == unit.Id).Select(item => item.PersonId).SingleAsync();
            var owner = ownerId == firstPerson.Id ? firstPerson : secondPerson;
            owner.UserId = user.Id;
            owner.UserLinkStatus = UserPersonLinkStatus.Verified;
            var ballot = new Ballot { OrganizationId = organization.Id, ZevId = zev.Id,
                CreatedByUserId = user.Id, Title = "Race", Question = "Vote?", Kind = BallotKind.Survey,
                Mode = VotingMode.OneUnitOneVote, Status = BallotStatus.Open,
                OpensUtc = DateTimeOffset.UtcNow.AddMinutes(-1), ClosesUtc = DateTimeOffset.UtcNow.AddMinutes(5),
                QuorumPercent = 50m, ApprovalPercent = 50m, EligibleWeight = 1m };
            var eligibility = new BallotEligibility { OrganizationId = organization.Id, ZevId = zev.Id,
                BallotId = ballot.Id, PropertyUnitId = unit.Id, PersonId = ownerId, Weight = 1m };
            database.AddRange(ballot, eligibility);
            await database.SaveChangesAsync();

            await using (var firstDatabase = new ZevDbContext(options, new HttpContextAccessor()))
            await using (var secondDatabase = new ZevDbContext(options, new HttpContextAccessor()))
            {
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                async Task<bool> CastAsync(ZevDbContext context)
                {
                    await start.Task;
                    return await new VotingService(context).CastAsync(zev.Id, ballot.Id, unit.Id,
                        user.Id, VoteChoice.Yes, CancellationToken.None);
                }
                var first = CastAsync(firstDatabase);
                var second = CastAsync(secondDatabase);
                start.SetResult();
                var results = await Task.WhenAll(first, second);
                Assert.Single(results, success => success);
                Assert.Single(await database.Votes.AsNoTracking().ToListAsync());
            }
        }
        finally
        {
            await database.Database.EnsureDeletedAsync();
        }
    }
}
