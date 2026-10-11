using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Zev.Api.Data;
using Zev.Api.Domain;

namespace Zev.Api.Tests;

// The running release is added to the version history at startup, once, without touching existing rows.
public sealed class AppVersionRecorderTests
{
    [Fact]
    public async Task CurrentVersionIsRecordedOnceAndExistingNotesAreKept()
    {
        var options = new DbContextOptionsBuilder<ZevDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var database = new ZevDbContext(options, new HttpContextAccessor());
        database.Database.EnsureCreated();
        var installed = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

        Assert.True(await AppVersionRecorder.EnsureCurrentAsync(database, installed, CancellationToken.None));
        var row = await database.VersionHistory.SingleAsync(item => item.Version == AppVersions.Current);
        Assert.Equal(AppVersions.CurrentTitle, row.Title);
        Assert.Equal("installed", row.Status);
        Assert.Equal(installed, row.InstalledAt);
        Assert.Equal(AppVersions.StartupInstalledBy, row.InstalledBy);

        row.AdminNotes = "Provjereno na UAT-u.";
        await database.SaveChangesAsync();
        Assert.False(await AppVersionRecorder.EnsureCurrentAsync(database, installed.AddDays(1), CancellationToken.None));
        database.ChangeTracker.Clear();
        var kept = await database.VersionHistory.SingleAsync(item => item.Version == AppVersions.Current);
        Assert.Equal("Provjereno na UAT-u.", kept.AdminNotes);
        Assert.Equal(installed, kept.InstalledAt);
        Assert.Equal(AppVersions.Seed.Length + 1, await database.VersionHistory.CountAsync());
    }

    [Fact]
    public void CurrentVersionIsNewerThanEveryMigratedRelease()
    {
        Assert.DoesNotContain(AppVersions.Seed, item => item.Version == AppVersions.Current);
        Assert.All(AppVersions.Seed, item =>
            Assert.True(AppVersionHistory.Parse(item.Version).CompareTo(AppVersionHistory.Parse(AppVersions.Current)) < 0, item.Version));
    }
}
