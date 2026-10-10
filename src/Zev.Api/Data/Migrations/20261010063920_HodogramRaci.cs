using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zev.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ZevDbContext))]
    [Migration("20261010063920_HodogramRaci")]
    public partial class HodogramRaci : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Hodograms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Hodograms", x => x.Id);
                    table.UniqueConstraint("AK_Hodograms_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_Hodograms_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HodogramPhases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HodogramId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HodogramPhases", x => x.Id);
                    table.UniqueConstraint("AK_HodogramPhases_OrganizationId_HodogramId_Id", x => new { x.OrganizationId, x.HodogramId, x.Id });
                    table.CheckConstraint("CK_HodogramPhases_SortOrder", "[SortOrder] >= 0");
                    table.ForeignKey(
                        name: "FK_HodogramPhases_Hodograms_OrganizationId_HodogramId",
                        columns: x => new { x.OrganizationId, x.HodogramId },
                        principalTable: "Hodograms",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HodogramSteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HodogramId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PhaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HodogramSteps", x => x.Id);
                    table.UniqueConstraint("AK_HodogramSteps_OrganizationId_HodogramId_Id", x => new { x.OrganizationId, x.HodogramId, x.Id });
                    table.CheckConstraint("CK_HodogramSteps_SortOrder", "[SortOrder] >= 0");
                    table.ForeignKey(
                        name: "FK_HodogramSteps_HodogramPhases_OrganizationId_HodogramId_PhaseId",
                        columns: x => new { x.OrganizationId, x.HodogramId, x.PhaseId },
                        principalTable: "HodogramPhases",
                        principalColumns: new[] { "OrganizationId", "HodogramId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HodogramAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HodogramId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StepId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HodogramAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HodogramAssignments_HodogramSteps_OrganizationId_HodogramId_StepId",
                        columns: x => new { x.OrganizationId, x.HodogramId, x.StepId },
                        principalTable: "HodogramSteps",
                        principalColumns: new[] { "OrganizationId", "HodogramId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HodogramAssignments_HodogramId",
                table: "HodogramAssignments",
                column: "HodogramId");

            migrationBuilder.CreateIndex(
                name: "IX_HodogramAssignments_OrganizationId_HodogramId_StepId",
                table: "HodogramAssignments",
                columns: new[] { "OrganizationId", "HodogramId", "StepId" });

            migrationBuilder.CreateIndex(
                name: "IX_HodogramAssignments_StepId_Role",
                table: "HodogramAssignments",
                columns: new[] { "StepId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HodogramPhases_HodogramId_SortOrder",
                table: "HodogramPhases",
                columns: new[] { "HodogramId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Hodograms_OrganizationId_Name",
                table: "Hodograms",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HodogramSteps_OrganizationId_HodogramId_PhaseId",
                table: "HodogramSteps",
                columns: new[] { "OrganizationId", "HodogramId", "PhaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_HodogramSteps_PhaseId_SortOrder",
                table: "HodogramSteps",
                columns: new[] { "PhaseId", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HodogramAssignments");

            migrationBuilder.DropTable(
                name: "HodogramSteps");

            migrationBuilder.DropTable(
                name: "HodogramPhases");

            migrationBuilder.DropTable(
                name: "Hodograms");
        }
    }
}
