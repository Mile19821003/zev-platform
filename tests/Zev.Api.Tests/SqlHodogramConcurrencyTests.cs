using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Hodograms;

namespace Zev.Api.Tests;

[Collection("Sql integration")]
public sealed class SqlHodogramConcurrencyTests
{
    [Fact]
    public async Task ConcurrentMatrixSavesOnSameRevisionKeepSingleWinner()
    {
        var connection = Environment.GetEnvironmentVariable("ZEV_TEST_SQL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var builder = new SqlConnectionStringBuilder(connection)
        {
            InitialCatalog = $"ZevHodogramRace_{Guid.NewGuid():N}"
        };
        var options = new DbContextOptionsBuilder<ZevDbContext>().UseSqlServer(builder.ConnectionString).Options;
        await using var database = new ZevDbContext(options, new HttpContextAccessor());
        try
        {
            await database.Database.MigrateAsync();
            var organization = new Organization { Code = "HRACE", Name = "Hodogram race" };
            var hodogram = new Hodogram { OrganizationId = organization.Id, Name = "Race" };
            var phase = new HodogramPhase { OrganizationId = organization.Id, HodogramId = hodogram.Id, Name = "Faza" };
            var step = new HodogramStep { OrganizationId = organization.Id, HodogramId = hodogram.Id,
                PhaseId = phase.Id, Name = "Korak" };
            database.AddRange(organization, hodogram, phase, step);
            await database.SaveChangesAsync();

            await using var firstDatabase = new ZevDbContext(options, new HttpContextAccessor());
            await using var secondDatabase = new ZevDbContext(options, new HttpContextAccessor());
            var bothLoaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var loaded = 0;
            async Task<bool> SaveAsync(ZevDbContext context, string responsibleRole)
            {
                var service = new HodogramService(context);
                var (tracked, stale) = await service.BeginUpdateAsync(organization.Id, hodogram.Id, 0, CancellationToken.None);
                Assert.False(stale);
                var result = await service.ApplyMatrixAsync(tracked!,
                [
                    new(step.Id, responsibleRole, "R"),
                    new(step.Id, nameof(MembershipRole.OrganizationAdministrator), "A")
                ], CancellationToken.None);
                Assert.Equal(MatrixSaveOutcome.Saved, result.Outcome);
                if (Interlocked.Increment(ref loaded) == 2) bothLoaded.SetResult();
                await bothLoaded.Task;
                try { await context.SaveChangesAsync(); return true; }
                catch (DbUpdateConcurrencyException) { return false; }
                catch (DbUpdateException problem) when (problem.InnerException is SqlException { Number: 2601 or 2627 })
                { return false; }
            }
            var results = await Task.WhenAll(SaveAsync(firstDatabase, nameof(MembershipRole.Manager)),
                SaveAsync(secondDatabase, nameof(MembershipRole.Accountant)));

            Assert.Single(results, saved => saved);
            Assert.Equal(1, await database.Hodograms.AsNoTracking().Where(item => item.Id == hodogram.Id)
                .Select(item => item.Revision).SingleAsync());
            var cells = await database.HodogramAssignments.AsNoTracking().Where(item => item.StepId == step.Id).ToListAsync();
            Assert.Equal(2, cells.Count);
            Assert.Single(cells, item => item.Value == RaciValue.R);
        }
        finally
        {
            await database.Database.EnsureDeletedAsync();
        }
    }
}
