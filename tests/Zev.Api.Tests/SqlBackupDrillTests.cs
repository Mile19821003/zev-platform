using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace Zev.Api.Tests;

[Collection("Sql integration")]
public sealed class SqlBackupDrillTests(ITestOutputHelper output)
{
    [Fact]
    public async Task BackupVerifyAndRestoreToSeparateDatabase()
    {
        var connectionString = Environment.GetEnvironmentVariable("ZEV_BACKUP_DRILL_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        var builder = new SqlConnectionStringBuilder(connectionString);
        Assert.Equal("Zev", builder.InitialCatalog);
        builder.InitialCatalog = "master";
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        const string backup = "/var/opt/mssql/data/Zev_TestBackup.bak";
        const string restored = "ZevRestoreTest";
        await using (var check = new SqlCommand("SELECT DB_ID(N'ZevRestoreTest')", connection))
            Assert.Equal(DBNull.Value, await check.ExecuteScalarAsync());
        await ExecuteAsync(connection, $"BACKUP DATABASE [Zev] TO DISK = N'{backup}' WITH COPY_ONLY, INIT, CHECKSUM");
        await ExecuteAsync(connection, $"RESTORE VERIFYONLY FROM DISK = N'{backup}' WITH CHECKSUM");
        var moves = new List<string>();
        await using (var files = new SqlCommand($"RESTORE FILELISTONLY FROM DISK = N'{backup}'", connection))
        await using (var reader = await files.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var logical = reader.GetString(reader.GetOrdinal("LogicalName")).Replace("'", "''");
                var kind = reader.GetString(reader.GetOrdinal("Type"));
                var extension = kind == "L" ? "ldf" : "mdf";
                moves.Add($"MOVE N'{logical}' TO N'/var/opt/mssql/data/{restored}_{moves.Count}.{extension}'");
            }
        }
        Assert.True(moves.Count >= 2);
        await ExecuteAsync(connection, $"RESTORE DATABASE [{restored}] FROM DISK = N'{backup}' WITH {string.Join(", ", moves)}, RECOVERY");
        try
        {
            await using var check = new SqlCommand("SELECT COUNT(*) FROM [ZevRestoreTest].[dbo].[__EFMigrationsHistory]", connection);
            var migrations = (int)(await check.ExecuteScalarAsync() ?? 0);
            Assert.True(migrations >= 8);
            await using var tables = new SqlCommand("SELECT COUNT(*) FROM [ZevRestoreTest].[sys].[tables]", connection);
            var tableCount = (int)(await tables.ExecuteScalarAsync() ?? 0);
            Assert.True(tableCount >= 20);
            await using var read = new SqlCommand("SELECT COUNT(*) FROM [ZevRestoreTest].[dbo].[Zevs]", connection);
            var zevs = (int)(await read.ExecuteScalarAsync() ?? 0);
            output.WriteLine($"Backup verified; restored database readable. Migrations={migrations}; tables={tableCount}; ZEVs={zevs}.");
        }
        finally
        {
            await ExecuteAsync(connection, $"ALTER DATABASE [{restored}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{restored}]");
        }
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 180 };
        await command.ExecuteNonQueryAsync();
    }
}
