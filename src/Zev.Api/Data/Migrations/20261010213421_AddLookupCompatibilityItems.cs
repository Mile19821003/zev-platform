using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Zev.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ZevDbContext))]
    [Migration("20261010213421_AddLookupCompatibilityItems")]
    public partial class AddLookupCompatibilityItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "LookupItems",
                columns: new[] { "Id", "Code", "CreatedUtc", "IsActive", "IsDefault", "LookupTypeCode", "Name", "SortOrder", "UpdatedByUserId", "UpdatedUtc", "ZevId" },
                // Migrations here have no Designer (target model), so the seed rows name their column types.
                columnTypes: new[] { "uniqueidentifier", "nvarchar(60)", "datetimeoffset", "bit", "bit", "nvarchar(60)", "nvarchar(200)", "int", "uniqueidentifier", "datetimeoffset", "uniqueidentifier" },
                values: new object[,]
                {
                    { new Guid("5e1f0000-0000-4000-8000-000000000032"), "Other", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, false, "PROPERTY_UNIT_TYPE", "Ostalo", 60, null, null, null },
                    { new Guid("5e1f0000-0000-4000-8000-000000000033"), "Migration", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, false, "PAYMENT_METHOD", "Migraciona uplata", 40, null, null, null },
                    { new Guid("5e1f0000-0000-4000-8000-000000000034"), "Other", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, false, "PAYMENT_METHOD", "Drugo", 50, null, null, null }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "LookupItems",
                keyColumn: "Id",
                keyColumnType: "uniqueidentifier",
                keyValue: new Guid("5e1f0000-0000-4000-8000-000000000032"));

            migrationBuilder.DeleteData(
                table: "LookupItems",
                keyColumn: "Id",
                keyColumnType: "uniqueidentifier",
                keyValue: new Guid("5e1f0000-0000-4000-8000-000000000033"));

            migrationBuilder.DeleteData(
                table: "LookupItems",
                keyColumn: "Id",
                keyColumnType: "uniqueidentifier",
                keyValue: new Guid("5e1f0000-0000-4000-8000-000000000034"));
        }
    }
}
