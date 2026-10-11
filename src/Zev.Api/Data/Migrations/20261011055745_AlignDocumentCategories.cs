using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Zev.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ZevDbContext))]
    [Migration("20261011055745_AlignDocumentCategories")]
    public partial class AlignDocumentCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "LookupItems",
                keyColumns: new[] { "Id" },
                keyColumnTypes: new[] { "uniqueidentifier" },
                keyValues: new object[] { new Guid("5e1f0000-0000-4000-8000-000000000022") },
                columns: new[] { "Code" },
                columnTypes: new[] { "nvarchar(60)" },
                values: new object[] { "InsurancePolicy" });

            migrationBuilder.UpdateData(
                table: "LookupItems",
                keyColumns: new[] { "Id" },
                keyColumnTypes: new[] { "uniqueidentifier" },
                keyValues: new object[] { new Guid("5e1f0000-0000-4000-8000-000000000023") },
                columns: new[] { "Code" },
                columnTypes: new[] { "nvarchar(60)" },
                values: new object[] { "Decision" });

            migrationBuilder.InsertData(
                table: "LookupItems",
                columns: new[] { "Id", "Code", "CreatedUtc", "IsActive", "IsDefault", "LookupTypeCode", "Name", "SortOrder", "UpdatedByUserId", "UpdatedUtc", "ZevId" },
                // Migrations here have no Designer (target model), so the seed rows name their column types.
                columnTypes: new[] { "uniqueidentifier", "nvarchar(60)", "datetimeoffset", "bit", "bit", "nvarchar(60)", "nvarchar(200)", "int", "uniqueidentifier", "datetimeoffset", "uniqueidentifier" },
                values: new object[,]
                {
                    { new Guid("5e1f0000-0000-4000-8000-000000000035"), "Policy", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, false, "DOCUMENT_CATEGORY", "Pravilnik", 60, null, null, null },
                    { new Guid("5e1f0000-0000-4000-8000-000000000036"), "Statute", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, false, "DOCUMENT_CATEGORY", "Statut", 70, null, null, null },
                    { new Guid("5e1f0000-0000-4000-8000-000000000037"), "Legal", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, false, "DOCUMENT_CATEGORY", "Pravni dokument", 80, null, null, null },
                    { new Guid("5e1f0000-0000-4000-8000-000000000038"), "Other", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, false, "DOCUMENT_CATEGORY", "Ostalo", 90, null, null, null }
                });

            migrationBuilder.InsertData(
                table: "ModuleIconSettings",
                columns: new[] { "Id", "IconKey", "ModuleKey", "UpdatedAt", "UpdatedByUserId" },
                columnTypes: new[] { "uniqueidentifier", "nvarchar(40)", "nvarchar(60)", "datetimeoffset", "uniqueidentifier" },
                values: new object[,]
                {
                    { new Guid("1c0e0000-0000-4000-8000-000000000035"), "activity", "errors", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { new Guid("1c0e0000-0000-4000-8000-000000000036"), "clipboard-check", "versions", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "LookupItems",
                keyColumn: "Id",
                keyColumnType: "uniqueidentifier",
                keyValue: new Guid("5e1f0000-0000-4000-8000-000000000035"));

            migrationBuilder.DeleteData(
                table: "LookupItems",
                keyColumn: "Id",
                keyColumnType: "uniqueidentifier",
                keyValue: new Guid("5e1f0000-0000-4000-8000-000000000036"));

            migrationBuilder.DeleteData(
                table: "LookupItems",
                keyColumn: "Id",
                keyColumnType: "uniqueidentifier",
                keyValue: new Guid("5e1f0000-0000-4000-8000-000000000037"));

            migrationBuilder.DeleteData(
                table: "LookupItems",
                keyColumn: "Id",
                keyColumnType: "uniqueidentifier",
                keyValue: new Guid("5e1f0000-0000-4000-8000-000000000038"));

            migrationBuilder.DeleteData(
                table: "ModuleIconSettings",
                keyColumn: "Id",
                keyColumnType: "uniqueidentifier",
                keyValue: new Guid("1c0e0000-0000-4000-8000-000000000035"));

            migrationBuilder.DeleteData(
                table: "ModuleIconSettings",
                keyColumn: "Id",
                keyColumnType: "uniqueidentifier",
                keyValue: new Guid("1c0e0000-0000-4000-8000-000000000036"));

            migrationBuilder.UpdateData(
                table: "LookupItems",
                keyColumns: new[] { "Id" },
                keyColumnTypes: new[] { "uniqueidentifier" },
                keyValues: new object[] { new Guid("5e1f0000-0000-4000-8000-000000000022") },
                columns: new[] { "Code" },
                columnTypes: new[] { "nvarchar(60)" },
                values: new object[] { "INSURANCE_POLICY" });

            migrationBuilder.UpdateData(
                table: "LookupItems",
                keyColumns: new[] { "Id" },
                keyColumnTypes: new[] { "uniqueidentifier" },
                keyValues: new object[] { new Guid("5e1f0000-0000-4000-8000-000000000023") },
                columns: new[] { "Code" },
                columnTypes: new[] { "nvarchar(60)" },
                values: new object[] { "DECISION" });
        }
    }
}
