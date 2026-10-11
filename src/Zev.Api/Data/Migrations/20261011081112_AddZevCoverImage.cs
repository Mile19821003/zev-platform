using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zev.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ZevDbContext))]
    [Migration("20261011081112_AddZevCoverImage")]
    public partial class AddZevCoverImage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageContentType",
                table: "Zevs",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageStorageKey",
                table: "Zevs",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "Zevs",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.InsertData(
                table: "AppVersionHistory",
                columns: new[] { "Version", "AdminNotes", "InstalledAt", "InstalledBy", "NotesUpdatedAt", "NotesUpdatedByUserId", "Status", "Title" },
                // Migrations here have no Designer (target model), so the seed rows name their column types.
                columnTypes: new[] { "nvarchar(20)", "nvarchar(2000)", "datetimeoffset", "nvarchar(120)", "datetimeoffset", "uniqueidentifier", "nvarchar(20)", "nvarchar(200)" },
                values: new object[] { "v2.15", null, new DateTimeOffset(new DateTime(2026, 10, 11, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Migracija baze", null, null, "installed", "Fotografija zgrade na karticama zajednica" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AppVersionHistory",
                keyColumn: "Version",
                keyColumnType: "nvarchar(20)",
                keyValue: "v2.15");

            migrationBuilder.DropColumn(
                name: "ImageContentType",
                table: "Zevs");

            migrationBuilder.DropColumn(
                name: "ImageStorageKey",
                table: "Zevs");

            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "Zevs");
        }
    }
}
