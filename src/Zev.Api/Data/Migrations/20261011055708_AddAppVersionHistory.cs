using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Zev.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ZevDbContext))]
    [Migration("20261011055708_AddAppVersionHistory")]
    public partial class AddAppVersionHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppVersionHistory",
                columns: table => new
                {
                    Version = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    InstalledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    InstalledBy = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    AdminNotes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    NotesUpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    NotesUpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppVersionHistory", x => x.Version);
                });

            migrationBuilder.InsertData(
                table: "AppVersionHistory",
                columns: new[] { "Version", "AdminNotes", "InstalledAt", "InstalledBy", "NotesUpdatedAt", "NotesUpdatedByUserId", "Status", "Title" },
                // Migrations here have no Designer (target model), so the seed rows name their column types.
                columnTypes: new[] { "nvarchar(20)", "nvarchar(2000)", "datetimeoffset", "nvarchar(120)", "datetimeoffset", "uniqueidentifier", "nvarchar(20)", "nvarchar(200)" },
                values: new object[,]
                {
                    { "v2.10", null, new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Migracija baze", null, null, "installed", "Prikazi i ikone: paleta ikonica za module i kategorije" },
                    { "v2.11", null, new DateTimeOffset(new DateTime(2026, 10, 11, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Migracija baze", null, null, "installed", "Šifarnici u formama (useLookup, storno ručne uplate)" },
                    { "v2.12", null, new DateTimeOffset(new DateTime(2026, 10, 11, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Migracija baze", null, null, "installed", "Greške u sistemu, istorija verzija i kategorije dokumenata" },
                    { "v2.7", null, new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Migracija baze", null, null, "installed", "Navigacija radnog prostora: kontrolna ploča i sklopivi meni" },
                    { "v2.8", null, new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Migracija baze", null, null, "installed", "Sistemska podešavanja i regionalni prikaz iznosa i datuma" },
                    { "v2.9", null, new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Migracija baze", null, null, "installed", "Šifarnici (generičke klasifikacije i padajuće liste)" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppVersionHistory");
        }
    }
}
