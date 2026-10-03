using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Zev.Api.Data.Migrations;

[DbContext(typeof(ZevDbContext))]
[Migration("20260930190000_InvoiceV2Stabilization")]
public sealed class InvoiceV2Stabilization : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>("RequiresBankAccountForInvoice", "Funds", "bit",
            nullable: false, defaultValue: false);
        migrationBuilder.AlterColumn<Guid>("TariffVersionId", "InvoiceLines", "uniqueidentifier",
            nullable: true, oldClrType: typeof(Guid), oldType: "uniqueidentifier");
        migrationBuilder.DropForeignKey("FK_Charges_TariffVersions_TariffVersionId", "Charges");
        migrationBuilder.DropIndex("IX_Charges_TariffVersionId", "Charges");
        migrationBuilder.DropIndex("IX_Charges_ZevId_Period_PropertyUnitId_PersonId_TariffVersionId", "Charges");
        migrationBuilder.AlterColumn<Guid>("TariffVersionId", "Charges", "uniqueidentifier",
            nullable: true, oldClrType: typeof(Guid), oldType: "uniqueidentifier");
        migrationBuilder.AddForeignKey("FK_Charges_TariffVersions_TariffVersionId", "Charges",
            "TariffVersionId", "TariffVersions", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.CreateIndex("IX_Charges_TariffVersionId", "Charges", "TariffVersionId");
        migrationBuilder.CreateIndex("IX_Charges_ZevId_Period_PropertyUnitId_PersonId_TariffVersionId",
            "Charges", new[] { "ZevId", "Period", "PropertyUnitId", "PersonId", "TariffVersionId" },
            unique: true, filter: "[BillingRunId] IS NOT NULL AND [TariffVersionId] IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Issued financial documents may contain free manual lines; restore a reviewed backup instead.");
}
