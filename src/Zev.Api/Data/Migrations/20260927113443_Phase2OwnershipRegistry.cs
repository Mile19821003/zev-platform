using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zev.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase2OwnershipRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContactEmail",
                table: "Zevs",
                type: "nvarchar(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactPhone",
                table: "Zevs",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "FoundedOn",
                table: "Zevs",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ActivatedOn",
                table: "Organizations",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressLine",
                table: "Organizations",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CityMunicipality",
                table: "Organizations",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactName",
                table: "Organizations",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DeactivatedOn",
                table: "Organizations",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Organizations",
                type: "nvarchar(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "Organizations",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaxIdentifier",
                table: "Organizations",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Entrances_OrganizationId_ZevId_BuildingId_Id",
                table: "Entrances",
                columns: new[] { "OrganizationId", "ZevId", "BuildingId", "Id" });

            migrationBuilder.CreateTable(
                name: "People",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TaxIdentifier = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    AddressLine = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    CityMunicipality = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_People", x => x.Id);
                    table.UniqueConstraint("AK_People_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_People_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_People_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PropertyUnits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZevId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntranceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Floor = table.Column<int>(type: "int", nullable: true),
                    AreaSquareMeters = table.Column<decimal>(type: "decimal(12,3)", precision: 12, scale: 3, nullable: false),
                    Coefficient = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    RegisteredMembers = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    OwnershipRevision = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PropertyUnits", x => x.Id);
                    table.UniqueConstraint("AK_PropertyUnits_OrganizationId_ZevId_Id", x => new { x.OrganizationId, x.ZevId, x.Id });
                    table.CheckConstraint("CK_PropertyUnits_Area", "[AreaSquareMeters] > 0");
                    table.CheckConstraint("CK_PropertyUnits_Coefficient", "[Coefficient] > 0");
                    table.CheckConstraint("CK_PropertyUnits_Members", "[RegisteredMembers] >= 0");
                    table.ForeignKey(
                        name: "FK_PropertyUnits_Entrances_OrganizationId_ZevId_BuildingId_EntranceId",
                        columns: x => new { x.OrganizationId, x.ZevId, x.BuildingId, x.EntranceId },
                        principalTable: "Entrances",
                        principalColumns: new[] { "OrganizationId", "ZevId", "BuildingId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Occupancies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZevId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PropertyUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidTo = table.Column<DateOnly>(type: "date", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Occupancies", x => x.Id);
                    table.CheckConstraint("CK_Occupancies_Dates", "[ValidTo] IS NULL OR [ValidTo] > [ValidFrom]");
                    table.ForeignKey(
                        name: "FK_Occupancies_People_OrganizationId_PersonId",
                        columns: x => new { x.OrganizationId, x.PersonId },
                        principalTable: "People",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Occupancies_PropertyUnits_OrganizationId_ZevId_PropertyUnitId",
                        columns: x => new { x.OrganizationId, x.ZevId, x.PropertyUnitId },
                        principalTable: "PropertyUnits",
                        principalColumns: new[] { "OrganizationId", "ZevId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Ownerships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZevId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PropertyUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidTo = table.Column<DateOnly>(type: "date", nullable: true),
                    Share = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ownerships", x => x.Id);
                    table.CheckConstraint("CK_Ownerships_Dates", "[ValidTo] IS NULL OR [ValidTo] > [ValidFrom]");
                    table.CheckConstraint("CK_Ownerships_Share", "[Share] > 0 AND [Share] <= 1");
                    table.ForeignKey(
                        name: "FK_Ownerships_People_OrganizationId_PersonId",
                        columns: x => new { x.OrganizationId, x.PersonId },
                        principalTable: "People",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Ownerships_PropertyUnits_OrganizationId_ZevId_PropertyUnitId",
                        columns: x => new { x.OrganizationId, x.ZevId, x.PropertyUnitId },
                        principalTable: "PropertyUnits",
                        principalColumns: new[] { "OrganizationId", "ZevId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Occupancies_OrganizationId_PersonId",
                table: "Occupancies",
                columns: new[] { "OrganizationId", "PersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_Occupancies_OrganizationId_ZevId_PropertyUnitId",
                table: "Occupancies",
                columns: new[] { "OrganizationId", "ZevId", "PropertyUnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_Occupancies_PropertyUnitId_PersonId_ValidFrom",
                table: "Occupancies",
                columns: new[] { "PropertyUnitId", "PersonId", "ValidFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Occupancies_ZevId_PropertyUnitId_ValidFrom",
                table: "Occupancies",
                columns: new[] { "ZevId", "PropertyUnitId", "ValidFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_Ownerships_OrganizationId_PersonId",
                table: "Ownerships",
                columns: new[] { "OrganizationId", "PersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_Ownerships_OrganizationId_ZevId_PropertyUnitId_ValidFrom",
                table: "Ownerships",
                columns: new[] { "OrganizationId", "ZevId", "PropertyUnitId", "ValidFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_Ownerships_PersonId_ValidFrom",
                table: "Ownerships",
                columns: new[] { "PersonId", "ValidFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_Ownerships_PropertyUnitId_PersonId_ValidFrom",
                table: "Ownerships",
                columns: new[] { "PropertyUnitId", "PersonId", "ValidFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_People_OrganizationId_DisplayName",
                table: "People",
                columns: new[] { "OrganizationId", "DisplayName" });

            migrationBuilder.CreateIndex(
                name: "IX_People_OrganizationId_UserId",
                table: "People",
                columns: new[] { "OrganizationId", "UserId" },
                unique: true,
                filter: "[UserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_People_UserId",
                table: "People",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PropertyUnits_OrganizationId_ZevId_BuildingId_EntranceId",
                table: "PropertyUnits",
                columns: new[] { "OrganizationId", "ZevId", "BuildingId", "EntranceId" });

            migrationBuilder.CreateIndex(
                name: "IX_PropertyUnits_OrganizationId_ZevId_EntranceId_Code",
                table: "PropertyUnits",
                columns: new[] { "OrganizationId", "ZevId", "EntranceId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PropertyUnits_ZevId_IsActive",
                table: "PropertyUnits",
                columns: new[] { "ZevId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Occupancies");

            migrationBuilder.DropTable(
                name: "Ownerships");

            migrationBuilder.DropTable(
                name: "People");

            migrationBuilder.DropTable(
                name: "PropertyUnits");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Entrances_OrganizationId_ZevId_BuildingId_Id",
                table: "Entrances");

            migrationBuilder.DropColumn(
                name: "ContactEmail",
                table: "Zevs");

            migrationBuilder.DropColumn(
                name: "ContactPhone",
                table: "Zevs");

            migrationBuilder.DropColumn(
                name: "FoundedOn",
                table: "Zevs");

            migrationBuilder.DropColumn(
                name: "ActivatedOn",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "AddressLine",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "CityMunicipality",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "ContactName",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "DeactivatedOn",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "TaxIdentifier",
                table: "Organizations");
        }
    }
}
