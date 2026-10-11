using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Zev.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ZevDbContext))]
    [Migration("20261011070734_AddVersionHistoryV213V214")]
    public partial class AddVersionHistoryV213V214 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "AppVersionHistory",
                columns: new[] { "Version", "AdminNotes", "InstalledAt", "InstalledBy", "NotesUpdatedAt", "NotesUpdatedByUserId", "Status", "Title" },
                // Migrations here have no Designer (target model), so the seed rows name their column types.
                columnTypes: new[] { "nvarchar(20)", "nvarchar(2000)", "datetimeoffset", "nvarchar(120)", "datetimeoffset", "uniqueidentifier", "nvarchar(20)", "nvarchar(200)" },
                values: new object[,]
                {
                    { "v2.13", null, new DateTimeOffset(new DateTime(2026, 10, 11, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Migracija baze", null, null, "installed", "Obrisani redovi: soft-delete i vraćanje zapisa (Recycle Bin)" },
                    { "v2.14", null, new DateTimeOffset(new DateTime(2026, 10, 11, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Migracija baze", null, null, "installed", "Teme i paleta boja (CSS varijable, pregled uživo)" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AppVersionHistory",
                keyColumn: "Version",
                keyColumnType: "nvarchar(20)",
                keyValue: "v2.13");

            migrationBuilder.DeleteData(
                table: "AppVersionHistory",
                keyColumn: "Version",
                keyColumnType: "nvarchar(20)",
                keyValue: "v2.14");
        }
    }
}
