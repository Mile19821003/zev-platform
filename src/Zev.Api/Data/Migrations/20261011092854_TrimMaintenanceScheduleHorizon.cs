using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zev.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ZevDbContext))]
    [Migration("20261011092854_TrimMaintenanceScheduleHorizon")]
    public partial class TrimMaintenanceScheduleHorizon : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Plans used to pre-generate a whole year of occurrences. Open ones beyond the 60-day horizon (no work order)
            // are removed; the calendar creates them again as their dates come closer.
            migrationBuilder.Sql("DELETE FROM [ScheduledMaintenances] WHERE [Status] = N'Due' AND [WorkOrderId] IS NULL "
                + "AND [DueOn] > DATEADD(day, 60, CAST(SYSUTCDATETIME() AS date));");

            migrationBuilder.InsertData(
                table: "AppVersionHistory",
                columns: new[] { "Version", "AdminNotes", "InstalledAt", "InstalledBy", "NotesUpdatedAt", "NotesUpdatedByUserId", "Status", "Title" },
                // Migrations here have no Designer (target model), so the seed rows name their column types.
                columnTypes: new[] { "nvarchar(20)", "nvarchar(2000)", "datetimeoffset", "nvarchar(120)", "datetimeoffset", "uniqueidentifier", "nvarchar(20)", "nvarchar(200)" },
                values: new object[] { "v2.16", null, new DateTimeOffset(new DateTime(2026, 10, 11, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Migracija baze", null, null, "installed", "Kalendar održavanja: statusi Planirano / Dospjelo / Završeno, horizont 60 dana" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AppVersionHistory",
                keyColumn: "Version",
                keyColumnType: "nvarchar(20)",
                keyValue: "v2.16");
        }
    }
}
