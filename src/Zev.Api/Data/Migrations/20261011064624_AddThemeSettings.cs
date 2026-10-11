using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zev.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ZevDbContext))]
    [Migration("20261011064624_AddThemeSettings")]
    public partial class AddThemeSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SystemThemeSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    ActiveThemeName = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    PrimaryColor = table.Column<string>(type: "varchar(7)", unicode: false, maxLength: 7, nullable: false),
                    SecondaryColor = table.Column<string>(type: "varchar(7)", unicode: false, maxLength: 7, nullable: false),
                    AccentColor = table.Column<string>(type: "varchar(7)", unicode: false, maxLength: 7, nullable: false),
                    BackgroundColor = table.Column<string>(type: "varchar(7)", unicode: false, maxLength: 7, nullable: false),
                    SurfaceColor = table.Column<string>(type: "varchar(7)", unicode: false, maxLength: 7, nullable: false),
                    TextColor = table.Column<string>(type: "varchar(7)", unicode: false, maxLength: 7, nullable: false),
                    HeaderBgColor = table.Column<string>(type: "varchar(7)", unicode: false, maxLength: 7, nullable: false),
                    HeaderTextColor = table.Column<string>(type: "varchar(7)", unicode: false, maxLength: 7, nullable: false),
                    MenuBgColor = table.Column<string>(type: "varchar(7)", unicode: false, maxLength: 7, nullable: false),
                    MenuTextColor = table.Column<string>(type: "varchar(7)", unicode: false, maxLength: 7, nullable: false),
                    MenuActiveColor = table.Column<string>(type: "varchar(7)", unicode: false, maxLength: 7, nullable: false),
                    BorderColor = table.Column<string>(type: "varchar(7)", unicode: false, maxLength: 7, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemThemeSettings", x => x.Id);
                    table.CheckConstraint("CK_SystemThemeSettings_Singleton", "[Id] = 1");
                    table.ForeignKey(
                        name: "FK_SystemThemeSettings_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "ModuleIconSettings",
                columns: new[] { "Id", "IconKey", "ModuleKey", "UpdatedAt", "UpdatedByUserId" },
                // Migrations here have no Designer (target model), so the seed rows name their column types.
                columnTypes: new[] { "uniqueidentifier", "nvarchar(40)", "nvarchar(60)", "datetimeoffset", "uniqueidentifier" },
                values: new object[] { new Guid("1c0e0000-0000-4000-8000-000000000038"), "monitor", "themes", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null });

            migrationBuilder.InsertData(
                table: "SystemThemeSettings",
                columns: new[] { "Id", "AccentColor", "ActiveThemeName", "BackgroundColor", "BorderColor", "HeaderBgColor", "HeaderTextColor", "MenuActiveColor", "MenuBgColor", "MenuTextColor", "PrimaryColor", "SecondaryColor", "SurfaceColor", "TextColor", "UpdatedAt", "UpdatedByUserId" },
                columnTypes: new[] { "int", "varchar(7)", "nvarchar(40)", "varchar(7)", "varchar(7)", "varchar(7)", "varchar(7)", "varchar(7)", "varchar(7)", "varchar(7)", "varchar(7)", "varchar(7)", "varchar(7)", "varchar(7)", "datetimeoffset", "uniqueidentifier" },
                values: new object[] { 1, "#e9f5ef", "Default", "#f5f7f8", "#e4e9ec", "#ffffff", "#17233a", "#23524d", "#102e31", "#e3eeeb", "#176d50", "#4f6070", "#ffffff", "#17233a", new DateTimeOffset(new DateTime(2026, 10, 11, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null });

            migrationBuilder.CreateIndex(
                name: "IX_SystemThemeSettings_UpdatedByUserId",
                table: "SystemThemeSettings",
                column: "UpdatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SystemThemeSettings");

            migrationBuilder.DeleteData(
                table: "ModuleIconSettings",
                keyColumn: "Id",
                keyColumnType: "uniqueidentifier",
                keyValue: new Guid("1c0e0000-0000-4000-8000-000000000038"));
        }
    }
}
