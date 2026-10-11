using Microsoft.EntityFrameworkCore;
using Zev.Api.Domain;

namespace Zev.Api.Data;

// Adds the running release (AppVersions.Current) to the version history the first time it starts against a database,
// so "Istorija verzija" stays current without a migration per release. Existing rows (and their notes) are kept.
public static class AppVersionRecorder
{
    public static async Task<bool> EnsureCurrentAsync(ZevDbContext database, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (await database.VersionHistory.AnyAsync(item => item.Version == AppVersions.Current, cancellationToken)) return false;
        database.VersionHistory.Add(new AppVersionHistory
        {
            Version = AppVersions.Current, Title = AppVersions.CurrentTitle, Status = "installed",
            InstalledAt = now, InstalledBy = AppVersions.StartupInstalledBy
        });
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // Another instance recorded it at the same moment (primary key on Version).
            database.ChangeTracker.Clear();
            return false;
        }
    }

    // Startup hook: only against a relational database (tests use the in-memory provider and their own seed); a
    // database that is not migrated yet must not stop the API from starting.
    public static async Task RecordAtStartupAsync(IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ZevDbContext>();
        if (!database.Database.IsRelational()) return;
        try
        {
            if (await EnsureCurrentAsync(database, DateTimeOffset.UtcNow, CancellationToken.None))
                logger.LogInformation("Version {Version} recorded in the version history.", AppVersions.Current);
        }
        catch (Exception exception) when (exception is DbUpdateException or InvalidOperationException
            or Microsoft.Data.SqlClient.SqlException)
        {
            logger.LogWarning(exception, "Version {Version} could not be recorded in the version history.", AppVersions.Current);
        }
    }
}
