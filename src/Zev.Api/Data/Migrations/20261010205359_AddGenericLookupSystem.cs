using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Zev.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ZevDbContext))]
    [Migration("20261010205359_AddGenericLookupSystem")]
    public partial class AddGenericLookupSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LookupTypes",
                columns: table => new
                {
                    Code = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LookupTypes", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "LookupItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LookupTypeCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    ZevId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LookupItems", x => x.Id);
                    table.CheckConstraint("CK_LookupItems_DefaultIsActive", "[IsDefault] = 0 OR [IsActive] = 1");
                    table.CheckConstraint("CK_LookupItems_SortOrder", "[SortOrder] >= 0");
                    table.ForeignKey(
                        name: "FK_LookupItems_LookupTypes_LookupTypeCode",
                        column: x => x.LookupTypeCode,
                        principalTable: "LookupTypes",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LookupItems_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LookupItems_Zevs_ZevId",
                        column: x => x.ZevId,
                        principalTable: "Zevs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "LookupTypes",
                columns: new[] { "Code", "Description", "IsSystem", "Name" },
                // Migrations here have no Designer (target model), so seed rows name their column types.
                columnTypes: new[] { "nvarchar(60)", "nvarchar(500)", "bit", "nvarchar(120)" },
                values: new object[,]
                {
                    { "DOCUMENT_CATEGORY", "Vrsta dokumenta zajednice", true, "Kategorije dokumenata" },
                    { "EXPENSE_CATEGORY", "Razvrstavanje ulaznih računa i rashoda", true, "Kategorije rashoda" },
                    { "MAINTENANCE_CATEGORY", "Vrsta prijave kvara ili radova", true, "Kategorije kvarova" },
                    { "MAINTENANCE_PRIORITY", "Hitnost prijave kvara", true, "Prioriteti održavanja" },
                    { "PAYMENT_METHOD", "Način ručne uplate", true, "Načini plaćanja" },
                    { "PROPERTY_UNIT_TYPE", "Stan, poslovni prostor, garaža…", true, "Vrste posebnih dijelova" },
                    { "REVERSAL_REASON", "Ponuđeni razlozi storna i ispravki", true, "Razlozi storna" }
                });

            migrationBuilder.InsertData(
                table: "LookupItems",
                columns: new[] { "Id", "Code", "CreatedUtc", "IsActive", "IsDefault", "LookupTypeCode", "Name", "SortOrder", "UpdatedByUserId", "UpdatedUtc", "ZevId" },
                columnTypes: new[] { "uniqueidentifier", "nvarchar(60)", "datetimeoffset", "bit", "bit", "nvarchar(60)", "nvarchar(200)", "int", "uniqueidentifier", "datetimeoffset", "uniqueidentifier" },
                values: new object[,]
                {
                    { new Guid("5e1f0000-0000-4000-8000-000000000001"), "Apartment", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, true, "PROPERTY_UNIT_TYPE", "Stan", 10, null, null, null },
                    { new Guid("5e1f0000-0000-4000-8000-000000000002"), "Commercial", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, false, "PROPERTY_UNIT_TYPE", "Poslovni prostor", 20, null, null, null },
                    { new Guid("5e1f0000-0000-4000-8000-000000000003"), "Garage", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, false, "PROPERTY_UNIT_TYPE", "Garaža", 30, null, null, null },
                    { new Guid("5e1f0000-0000-4000-8000-000000000004"), "ParkingSpace", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, false, "PROPERTY_UNIT_TYPE", "Parking mjesto", 40, null, null, null },
                    { new Guid("5e1f0000-0000-4000-8000-000000000005"), "Basement", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, false, "PROPERTY_UNIT_TYPE", "Ostava", 50, null, null, null },
                    { new Guid("5e1f0000-0000-4000-8000-000000000006"), "CURRENT_MAINTENANCE", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, false, "EXPENSE_CATEGORY", "Tekuće održavanje", 10, null, null, null },
                    { new Guid("5e1f0000-0000-4000-8000-000000000007"), "CAPITAL_MAINTENANCE", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, false, "EXPENSE_CATEGORY", "Investiciono održavanje", 20, null, null, null },
                    { new Guid("5e1f0000-0000-4000-8000-000000000008"), "UTILITIES", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, false, "EXPENSE_CATEGORY", "Komunalne usluge", 30, null, null, null },
                    { new Guid("5e1f0000-0000-4000-8000-000000000009"), "ELEVATOR", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, false, "EXPENSE_CATEGORY", "Lift", 40, null, null, null },
                    { new Guid("5e1f0000-0000-4000-8000-000000000010"), "MANAGER", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, false, "EXPENSE_CATEGORY", "Upravnik", 50, null, null, null },
                    { new Guid("5e1f0000-0000-4000-8000-000000000011"), "PLUMBING", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, false, "MAINTENANCE_CATEGORY", "Vodovod", 10, null, null, null },
                    { new Guid("5e1f0000-0000-4000-8000-000000000012"), "ELECTRICAL", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, false, "MAINTENANCE_CATEGORY", "Elektroinstalacije", 20, null, null, null },
                    { new Guid("5e1f0000-0000-4000-8000-000000000013"), "CONSTRUCTION", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, false, "MAINTENANCE_CATEGORY", "Građevinski radovi", 30, null, null, null },
                    { new Guid("5e1f0000-0000-4000-8000-000000000014"), "ROOF", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, false, "MAINTENANCE_CATEGORY", "Krov", 40, null, null, null },
                    { new Guid("5e1f0000-0000-4000-8000-000000000015"), "LOCKSMITH", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, false, "MAINTENANCE_CATEGORY", "Bravarija", 50, null, null, null },
                    { new Guid("5e1f0000-0000-4000-8000-000000000016"), "Urgent", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, false, "MAINTENANCE_PRIORITY", "Hitno", 10, null, null, null },
                    { new Guid("5e1f0000-0000-4000-8000-000000000017"), "High", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, false, "MAINTENANCE_PRIORITY", "Visok", 20, null, null, null },
                    { new Guid("5e1f0000-0000-4000-8000-000000000018"), "Normal", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, true, "MAINTENANCE_PRIORITY", "Standardni", 30, null, null, null },
                    { new Guid("5e1f0000-0000-4000-8000-000000000019"), "Low", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, false, "MAINTENANCE_PRIORITY", "Nizak", 40, null, null, null },
                    { new Guid("5e1f0000-0000-4000-8000-000000000020"), "Minutes", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, false, "DOCUMENT_CATEGORY", "Zapisnik", 10, null, null, null },
                    { new Guid("5e1f0000-0000-4000-8000-000000000021"), "Contract", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, false, "DOCUMENT_CATEGORY", "Ugovor", 20, null, null, null },
                    { new Guid("5e1f0000-0000-4000-8000-000000000022"), "INSURANCE_POLICY", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, false, "DOCUMENT_CATEGORY", "Polisa osiguranja", 30, null, null, null },
                    { new Guid("5e1f0000-0000-4000-8000-000000000023"), "DECISION", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, false, "DOCUMENT_CATEGORY", "Odluka", 40, null, null, null },
                    { new Guid("5e1f0000-0000-4000-8000-000000000024"), "Financial", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, false, "DOCUMENT_CATEGORY", "Finansijski izvještaj", 50, null, null, null },
                    { new Guid("5e1f0000-0000-4000-8000-000000000025"), "Cash", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, false, "PAYMENT_METHOD", "Gotovina", 10, null, null, null },
                    { new Guid("5e1f0000-0000-4000-8000-000000000026"), "Transfer", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, true, "PAYMENT_METHOD", "Virman", 20, null, null, null },
                    { new Guid("5e1f0000-0000-4000-8000-000000000027"), "PaymentSlip", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, false, "PAYMENT_METHOD", "Uplatnica", 30, null, null, null },
                    { new Guid("5e1f0000-0000-4000-8000-000000000028"), "WRONG_ENTRY", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, false, "REVERSAL_REASON", "Pogrešan unos", 10, null, null, null },
                    { new Guid("5e1f0000-0000-4000-8000-000000000029"), "DUPLICATE", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, false, "REVERSAL_REASON", "Dvostruka uplata", 20, null, null, null },
                    { new Guid("5e1f0000-0000-4000-8000-000000000030"), "COMPLAINT", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, false, "REVERSAL_REASON", "Reklamacija", 30, null, null, null },
                    { new Guid("5e1f0000-0000-4000-8000-000000000031"), "AMOUNT_CORRECTION", new DateTimeOffset(new DateTime(2026, 10, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, false, "REVERSAL_REASON", "Ispravak iznosa", 40, null, null, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_LookupItems_Default",
                table: "LookupItems",
                columns: new[] { "LookupTypeCode", "ZevId" },
                unique: true,
                filter: "[IsDefault] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_LookupItems_LookupTypeCode_ZevId_Code",
                table: "LookupItems",
                columns: new[] { "LookupTypeCode", "ZevId", "Code" },
                unique: true,
                filter: "[ZevId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LookupItems_UpdatedByUserId",
                table: "LookupItems",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LookupItems_ZevId",
                table: "LookupItems",
                column: "ZevId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LookupItems");

            migrationBuilder.DropTable(
                name: "LookupTypes");
        }
    }
}
