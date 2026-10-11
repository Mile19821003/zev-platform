using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zev.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ZevDbContext))]
    [Migration("20261011125900_AddMaintenanceCompleteWorkflow")]
    public partial class AddMaintenanceCompleteWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CompletedByUserId",
                table: "WorkOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "CompletedDate",
                table: "WorkOrders",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContractorName",
                table: "WorkOrders",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContractorPhone",
                table: "WorkOrders",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExpenseFundKind",
                table: "WorkOrders",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExpenseId",
                table: "WorkOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceNumber",
                table: "WorkOrders",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkDescription",
                table: "WorkOrders",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WorkOrderAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZevId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttachmentType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Length = table.Column<int>(type: "int", nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkOrderAttachments_WorkOrders_OrganizationId_ZevId_WorkOrderId",
                        columns: x => new { x.OrganizationId, x.ZevId, x.WorkOrderId },
                        principalTable: "WorkOrders",
                        principalColumns: new[] { "OrganizationId", "ZevId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_ExpenseId",
                table: "WorkOrders",
                column: "ExpenseId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderAttachments_OrganizationId_ZevId_WorkOrderId",
                table: "WorkOrderAttachments",
                columns: new[] { "OrganizationId", "ZevId", "WorkOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderAttachments_WorkOrderId_AttachmentType",
                table: "WorkOrderAttachments",
                columns: new[] { "WorkOrderId", "AttachmentType" });

            migrationBuilder.AddForeignKey(
                name: "FK_WorkOrders_Expenses_ExpenseId",
                table: "WorkOrders",
                column: "ExpenseId",
                principalTable: "Expenses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkOrders_Expenses_ExpenseId",
                table: "WorkOrders");

            migrationBuilder.DropTable(
                name: "WorkOrderAttachments");

            migrationBuilder.DropIndex(
                name: "IX_WorkOrders_ExpenseId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "CompletedByUserId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "CompletedDate",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "ContractorName",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "ContractorPhone",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "ExpenseFundKind",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "ExpenseId",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "InvoiceNumber",
                table: "WorkOrders");

            migrationBuilder.DropColumn(
                name: "WorkDescription",
                table: "WorkOrders");
        }
    }
}
