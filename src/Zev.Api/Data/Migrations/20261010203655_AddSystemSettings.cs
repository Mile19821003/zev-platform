using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zev.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ZevDbContext))]
    [Migration("20261010203655_AddSystemSettings")]
    public partial class AddSystemSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SystemSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    DecimalSeparator = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: false),
                    DateFormat = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FontSize = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    DefaultTheme = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSettings", x => x.Id);
                    table.CheckConstraint("CK_SystemSettings_DateFormat", "[DateFormat] IN ('DD.MM.YYYY', 'YYYY-MM-DD')");
                    table.CheckConstraint("CK_SystemSettings_DecimalSeparator", "[DecimalSeparator] IN (',', '.')");
                    table.CheckConstraint("CK_SystemSettings_FontSize", "[FontSize] IN ('13px', '14px', '15px')");
                    table.CheckConstraint("CK_SystemSettings_Singleton", "[Id] = 1");
                    table.ForeignKey(
                        name: "FK_SystemSettings_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "SystemSettings",
                columns: new[] { "Id", "DateFormat", "DecimalSeparator", "DefaultTheme", "FontSize", "UpdatedAt", "UpdatedByUserId" },
                // Migrations here have no Designer (target model), so the seed row names its column types.
                columnTypes: new[] { "int", "nvarchar(20)", "nvarchar(1)", "nvarchar(40)", "nvarchar(10)", "datetimeoffset", "uniqueidentifier" },
                values: new object[] { 1, "DD.MM.YYYY", ",", "Default", "13px", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null });

            migrationBuilder.CreateIndex(
                name: "IX_SystemSettings_UpdatedByUserId",
                table: "SystemSettings",
                column: "UpdatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SystemSettings");
        }
    }
}
