using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zev.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ZevDbContext))]
    [Migration("20261010092746_PaymentAccountsAndExpenseCurrency")]
    public partial class PaymentAccountsAndExpenseCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                table: "Expenses",
                type: "nvarchar(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "BAM");

            // Existing expenses take their fund's currency: the single currency of the fund's active bank
            // accounts, BAM (Finance:DefaultCurrency default) without accounts, UNKNOWN when ambiguous.
            // EXEC defers compilation: in the idempotent script the column is added in the same batch.
            migrationBuilder.Sql("""
                EXEC(N'UPDATE e SET CurrencyCode = CASE WHEN c.Currencies > 1 THEN N''UNKNOWN''
                    WHEN c.Currencies = 1 THEN c.Currency ELSE N''BAM'' END
                FROM Expenses AS e
                OUTER APPLY (SELECT COUNT(DISTINCT UPPER(b.CurrencyCode)) AS Currencies,
                    MIN(UPPER(b.CurrencyCode)) AS Currency
                    FROM BankAccounts AS b
                    WHERE b.ZevId = e.ZevId AND b.FundId = e.FundId AND b.IsActive = 1) AS c;');
                """);

            migrationBuilder.CreateTable(
                name: "PaymentAccounts",
                columns: table => new
                {
                    Number = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZevId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PropertyUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentAccounts", x => x.Number);
                    table.CheckConstraint("CK_PaymentAccounts_Number", "[Number] BETWEEN 1 AND 9999999999");
                    table.ForeignKey(
                        name: "FK_PaymentAccounts_People_OrganizationId_PersonId",
                        columns: x => new { x.OrganizationId, x.PersonId },
                        principalTable: "People",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentAccounts_PropertyUnits_OrganizationId_ZevId_PropertyUnitId",
                        columns: x => new { x.OrganizationId, x.ZevId, x.PropertyUnitId },
                        principalTable: "PropertyUnits",
                        principalColumns: new[] { "OrganizationId", "ZevId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentAccounts_Zevs_OrganizationId_ZevId",
                        columns: x => new { x.OrganizationId, x.ZevId },
                        principalTable: "Zevs",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAccounts_OrganizationId_PersonId",
                table: "PaymentAccounts",
                columns: new[] { "OrganizationId", "PersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAccounts_OrganizationId_ZevId_PropertyUnitId",
                table: "PaymentAccounts",
                columns: new[] { "OrganizationId", "ZevId", "PropertyUnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAccounts_ZevId_PropertyUnitId_PersonId",
                table: "PaymentAccounts",
                columns: new[] { "ZevId", "PropertyUnitId", "PersonId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentAccounts");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                table: "Expenses");
        }
    }
}
