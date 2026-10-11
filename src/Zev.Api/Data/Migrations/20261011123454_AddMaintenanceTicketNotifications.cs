using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zev.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ZevDbContext))]
    [Migration("20261011123454_AddMaintenanceTicketNotifications")]
    public partial class AddMaintenanceTicketNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultAdminEmail",
                table: "SystemSettings",
                type: "nvarchar(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsReadByAdmin",
                table: "MaintenanceTickets",
                type: "bit",
                nullable: false,
                // Reports made before notifications existed are not "new": the bell starts empty.
                defaultValue: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReadByAdminAt",
                table: "MaintenanceTickets",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReadByAdminUserId",
                table: "MaintenanceTickets",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceTickets_ZevId_IsReadByAdmin",
                table: "MaintenanceTickets",
                columns: new[] { "ZevId", "IsReadByAdmin" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MaintenanceTickets_ZevId_IsReadByAdmin",
                table: "MaintenanceTickets");

            migrationBuilder.DropColumn(
                name: "DefaultAdminEmail",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "IsReadByAdmin",
                table: "MaintenanceTickets");

            migrationBuilder.DropColumn(
                name: "ReadByAdminAt",
                table: "MaintenanceTickets");

            migrationBuilder.DropColumn(
                name: "ReadByAdminUserId",
                table: "MaintenanceTickets");
        }
    }
}
