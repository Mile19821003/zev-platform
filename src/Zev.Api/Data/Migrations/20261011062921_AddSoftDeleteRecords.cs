using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zev.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ZevDbContext))]
    [Migration("20261011062921_AddSoftDeleteRecords")]
    public partial class AddSoftDeleteRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PropertyUnits_OrganizationId_ZevId_EntranceId_Code",
                table: "PropertyUnits");

            migrationBuilder.DropIndex(
                name: "IX_People_OrganizationId_UserId",
                table: "People");

            migrationBuilder.DropIndex(
                name: "IX_Entrances_OrganizationId_ZevId_BuildingId_Code",
                table: "Entrances");

            migrationBuilder.DropIndex(
                name: "IX_Buildings_OrganizationId_ZevId_Code",
                table: "Buildings");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "Suppliers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedByUserId",
                table: "Suppliers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Suppliers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "PropertyUnits",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedByUserId",
                table: "PropertyUnits",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "PropertyUnits",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "People",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedByUserId",
                table: "People",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "People",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "Entrances",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedByUserId",
                table: "Entrances",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Entrances",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "Buildings",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeletedByUserId",
                table: "Buildings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Buildings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.InsertData(
                table: "ModuleIconSettings",
                columns: new[] { "Id", "IconKey", "ModuleKey", "UpdatedAt", "UpdatedByUserId" },
                // Migrations here have no Designer (target model), so the seed rows name their column types.
                columnTypes: new[] { "uniqueidentifier", "nvarchar(40)", "nvarchar(60)", "datetimeoffset", "uniqueidentifier" },
                values: new object[] { new Guid("1c0e0000-0000-4000-8000-000000000037"), "package", "deleted", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null });

            migrationBuilder.CreateIndex(
                name: "IX_PropertyUnits_OrganizationId_ZevId_EntranceId_Code",
                table: "PropertyUnits",
                columns: new[] { "OrganizationId", "ZevId", "EntranceId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_People_OrganizationId_UserId",
                table: "People",
                columns: new[] { "OrganizationId", "UserId" },
                unique: true,
                filter: "[UserId] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Entrances_OrganizationId_ZevId_BuildingId_Code",
                table: "Entrances",
                columns: new[] { "OrganizationId", "ZevId", "BuildingId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Buildings_OrganizationId_ZevId_Code",
                table: "Buildings",
                columns: new[] { "OrganizationId", "ZevId", "Code" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PropertyUnits_OrganizationId_ZevId_EntranceId_Code",
                table: "PropertyUnits");

            migrationBuilder.DropIndex(
                name: "IX_People_OrganizationId_UserId",
                table: "People");

            migrationBuilder.DropIndex(
                name: "IX_Entrances_OrganizationId_ZevId_BuildingId_Code",
                table: "Entrances");

            migrationBuilder.DropIndex(
                name: "IX_Buildings_OrganizationId_ZevId_Code",
                table: "Buildings");

            migrationBuilder.DeleteData(
                table: "ModuleIconSettings",
                keyColumn: "Id",
                keyColumnType: "uniqueidentifier",
                keyValue: new Guid("1c0e0000-0000-4000-8000-000000000037"));

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "PropertyUnits");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "PropertyUnits");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "PropertyUnits");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "People");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "People");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "People");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Entrances");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "Entrances");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Entrances");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "Buildings");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Buildings");

            migrationBuilder.CreateIndex(
                name: "IX_PropertyUnits_OrganizationId_ZevId_EntranceId_Code",
                table: "PropertyUnits",
                columns: new[] { "OrganizationId", "ZevId", "EntranceId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_People_OrganizationId_UserId",
                table: "People",
                columns: new[] { "OrganizationId", "UserId" },
                unique: true,
                filter: "[UserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Entrances_OrganizationId_ZevId_BuildingId_Code",
                table: "Entrances",
                columns: new[] { "OrganizationId", "ZevId", "BuildingId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Buildings_OrganizationId_ZevId_Code",
                table: "Buildings",
                columns: new[] { "OrganizationId", "ZevId", "Code" },
                unique: true);
        }
    }
}
