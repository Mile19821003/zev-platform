using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zev.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase6Maintenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MaintenancePlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZevId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FrequencyDays = table.Column<int>(type: "int", nullable: false),
                    FirstDueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenancePlans", x => x.Id);
                    table.UniqueConstraint("AK_MaintenancePlans_OrganizationId_ZevId_Id", x => new { x.OrganizationId, x.ZevId, x.Id });
                    table.CheckConstraint("CK_MaintenancePlans_Frequency", "[FrequencyDays] > 0");
                    table.ForeignKey(
                        name: "FK_MaintenancePlans_Zevs_OrganizationId_ZevId",
                        columns: x => new { x.OrganizationId, x.ZevId },
                        principalTable: "Zevs",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceTickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZevId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PropertyUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReportedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Location = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceTickets", x => x.Id);
                    table.UniqueConstraint("AK_MaintenanceTickets_OrganizationId_ZevId_Id", x => new { x.OrganizationId, x.ZevId, x.Id });
                    table.ForeignKey(
                        name: "FK_MaintenanceTickets_PropertyUnits_OrganizationId_ZevId_PropertyUnitId",
                        columns: x => new { x.OrganizationId, x.ZevId, x.PropertyUnitId },
                        principalTable: "PropertyUnits",
                        principalColumns: new[] { "OrganizationId", "ZevId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaintenanceTickets_Users_ReportedByUserId",
                        column: x => x.ReportedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaintenanceTickets_Zevs_OrganizationId_ZevId",
                        columns: x => new { x.OrganizationId, x.ZevId },
                        principalTable: "Zevs",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceTicketFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZevId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TicketId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoredFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceTicketFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaintenanceTicketFiles_MaintenanceTickets_OrganizationId_ZevId_TicketId",
                        columns: x => new { x.OrganizationId, x.ZevId, x.TicketId },
                        principalTable: "MaintenanceTickets",
                        principalColumns: new[] { "OrganizationId", "ZevId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaintenanceTicketFiles_StoredFiles_OrganizationId_ZevId_StoredFileId",
                        columns: x => new { x.OrganizationId, x.ZevId, x.StoredFileId },
                        principalTable: "StoredFiles",
                        principalColumns: new[] { "OrganizationId", "ZevId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZevId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TicketId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CostAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrders", x => x.Id);
                    table.UniqueConstraint("AK_WorkOrders_OrganizationId_ZevId_Id", x => new { x.OrganizationId, x.ZevId, x.Id });
                    table.CheckConstraint("CK_WorkOrders_Cost", "[CostAmount] IS NULL OR [CostAmount] >= 0");
                    table.ForeignKey(
                        name: "FK_WorkOrders_MaintenanceTickets_OrganizationId_ZevId_TicketId",
                        columns: x => new { x.OrganizationId, x.ZevId, x.TicketId },
                        principalTable: "MaintenanceTickets",
                        principalColumns: new[] { "OrganizationId", "ZevId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrders_Suppliers_OrganizationId_SupplierId",
                        columns: x => new { x.OrganizationId, x.SupplierId },
                        principalTable: "Suppliers",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrders_Zevs_OrganizationId_ZevId",
                        columns: x => new { x.OrganizationId, x.ZevId },
                        principalTable: "Zevs",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceStatusEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZevId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TicketId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceStatusEvents", x => x.Id);
                    table.CheckConstraint("CK_MaintenanceStatusEvents_Target", "([TicketId] IS NULL AND [WorkOrderId] IS NOT NULL) OR ([TicketId] IS NOT NULL AND [WorkOrderId] IS NULL)");
                    table.ForeignKey(
                        name: "FK_MaintenanceStatusEvents_MaintenanceTickets_OrganizationId_ZevId_TicketId",
                        columns: x => new { x.OrganizationId, x.ZevId, x.TicketId },
                        principalTable: "MaintenanceTickets",
                        principalColumns: new[] { "OrganizationId", "ZevId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaintenanceStatusEvents_WorkOrders_OrganizationId_ZevId_WorkOrderId",
                        columns: x => new { x.OrganizationId, x.ZevId, x.WorkOrderId },
                        principalTable: "WorkOrders",
                        principalColumns: new[] { "OrganizationId", "ZevId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ScheduledMaintenances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZevId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledMaintenances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduledMaintenances_MaintenancePlans_OrganizationId_ZevId_PlanId",
                        columns: x => new { x.OrganizationId, x.ZevId, x.PlanId },
                        principalTable: "MaintenancePlans",
                        principalColumns: new[] { "OrganizationId", "ZevId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduledMaintenances_WorkOrders_OrganizationId_ZevId_WorkOrderId",
                        columns: x => new { x.OrganizationId, x.ZevId, x.WorkOrderId },
                        principalTable: "WorkOrders",
                        principalColumns: new[] { "OrganizationId", "ZevId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZevId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoredFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkOrderFiles_StoredFiles_OrganizationId_ZevId_StoredFileId",
                        columns: x => new { x.OrganizationId, x.ZevId, x.StoredFileId },
                        principalTable: "StoredFiles",
                        principalColumns: new[] { "OrganizationId", "ZevId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrderFiles_WorkOrders_OrganizationId_ZevId_WorkOrderId",
                        columns: x => new { x.OrganizationId, x.ZevId, x.WorkOrderId },
                        principalTable: "WorkOrders",
                        principalColumns: new[] { "OrganizationId", "ZevId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenancePlans_ZevId_IsActive_FirstDueOn",
                table: "MaintenancePlans",
                columns: new[] { "ZevId", "IsActive", "FirstDueOn" });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceStatusEvents_OrganizationId_ZevId_TicketId",
                table: "MaintenanceStatusEvents",
                columns: new[] { "OrganizationId", "ZevId", "TicketId" });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceStatusEvents_OrganizationId_ZevId_WorkOrderId",
                table: "MaintenanceStatusEvents",
                columns: new[] { "OrganizationId", "ZevId", "WorkOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceStatusEvents_TicketId_ChangedUtc",
                table: "MaintenanceStatusEvents",
                columns: new[] { "TicketId", "ChangedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceStatusEvents_WorkOrderId_ChangedUtc",
                table: "MaintenanceStatusEvents",
                columns: new[] { "WorkOrderId", "ChangedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceTicketFiles_OrganizationId_ZevId_StoredFileId",
                table: "MaintenanceTicketFiles",
                columns: new[] { "OrganizationId", "ZevId", "StoredFileId" });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceTicketFiles_OrganizationId_ZevId_TicketId",
                table: "MaintenanceTicketFiles",
                columns: new[] { "OrganizationId", "ZevId", "TicketId" });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceTicketFiles_TicketId_StoredFileId",
                table: "MaintenanceTicketFiles",
                columns: new[] { "TicketId", "StoredFileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceTickets_OrganizationId_ZevId_PropertyUnitId",
                table: "MaintenanceTickets",
                columns: new[] { "OrganizationId", "ZevId", "PropertyUnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceTickets_ReportedByUserId",
                table: "MaintenanceTickets",
                column: "ReportedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceTickets_ZevId_Status_CreatedUtc",
                table: "MaintenanceTickets",
                columns: new[] { "ZevId", "Status", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledMaintenances_OrganizationId_ZevId_PlanId",
                table: "ScheduledMaintenances",
                columns: new[] { "OrganizationId", "ZevId", "PlanId" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledMaintenances_OrganizationId_ZevId_WorkOrderId",
                table: "ScheduledMaintenances",
                columns: new[] { "OrganizationId", "ZevId", "WorkOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledMaintenances_PlanId_DueOn",
                table: "ScheduledMaintenances",
                columns: new[] { "PlanId", "DueOn" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledMaintenances_ZevId_Status_DueOn",
                table: "ScheduledMaintenances",
                columns: new[] { "ZevId", "Status", "DueOn" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderFiles_OrganizationId_ZevId_StoredFileId",
                table: "WorkOrderFiles",
                columns: new[] { "OrganizationId", "ZevId", "StoredFileId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderFiles_OrganizationId_ZevId_WorkOrderId",
                table: "WorkOrderFiles",
                columns: new[] { "OrganizationId", "ZevId", "WorkOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderFiles_WorkOrderId_StoredFileId",
                table: "WorkOrderFiles",
                columns: new[] { "WorkOrderId", "StoredFileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_OrganizationId_SupplierId",
                table: "WorkOrders",
                columns: new[] { "OrganizationId", "SupplierId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_OrganizationId_ZevId_TicketId",
                table: "WorkOrders",
                columns: new[] { "OrganizationId", "ZevId", "TicketId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_ZevId_Status_DueOn",
                table: "WorkOrders",
                columns: new[] { "ZevId", "Status", "DueOn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaintenanceStatusEvents");

            migrationBuilder.DropTable(
                name: "MaintenanceTicketFiles");

            migrationBuilder.DropTable(
                name: "ScheduledMaintenances");

            migrationBuilder.DropTable(
                name: "WorkOrderFiles");

            migrationBuilder.DropTable(
                name: "MaintenancePlans");

            migrationBuilder.DropTable(
                name: "WorkOrders");

            migrationBuilder.DropTable(
                name: "MaintenanceTickets");
        }
    }
}
