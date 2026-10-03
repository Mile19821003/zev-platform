using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zev.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase2Registry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AddressLine",
                table: "Zevs",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CityMunicipality",
                table: "Zevs",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryBankAccount",
                table: "Zevs",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReserveFundBankAccount",
                table: "Zevs",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaxIdentifier",
                table: "Zevs",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Buildings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZevId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AddressLine = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Buildings", x => x.Id);
                    table.UniqueConstraint("AK_Buildings_OrganizationId_ZevId_Id", x => new { x.OrganizationId, x.ZevId, x.Id });
                    table.ForeignKey(
                        name: "FK_Buildings_Zevs_OrganizationId_ZevId",
                        columns: x => new { x.OrganizationId, x.ZevId },
                        principalTable: "Zevs",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Entrances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZevId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Entrances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Entrances_Buildings_OrganizationId_ZevId_BuildingId",
                        columns: x => new { x.OrganizationId, x.ZevId, x.BuildingId },
                        principalTable: "Buildings",
                        principalColumns: new[] { "OrganizationId", "ZevId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Buildings_OrganizationId_ZevId_Code",
                table: "Buildings",
                columns: new[] { "OrganizationId", "ZevId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Buildings_ZevId_IsActive",
                table: "Buildings",
                columns: new[] { "ZevId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Entrances_OrganizationId_ZevId_BuildingId_Code",
                table: "Entrances",
                columns: new[] { "OrganizationId", "ZevId", "BuildingId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Entrances_ZevId_BuildingId_IsActive",
                table: "Entrances",
                columns: new[] { "ZevId", "BuildingId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Entrances");

            migrationBuilder.DropTable(
                name: "Buildings");

            migrationBuilder.DropColumn(
                name: "AddressLine",
                table: "Zevs");

            migrationBuilder.DropColumn(
                name: "CityMunicipality",
                table: "Zevs");

            migrationBuilder.DropColumn(
                name: "PrimaryBankAccount",
                table: "Zevs");

            migrationBuilder.DropColumn(
                name: "ReserveFundBankAccount",
                table: "Zevs");

            migrationBuilder.DropColumn(
                name: "TaxIdentifier",
                table: "Zevs");
        }
    }
}
