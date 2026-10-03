using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Zev.Api.Data.Migrations;

[DbContext(typeof(ZevDbContext))]
[Migration("20260928120000_BillingItemWorkflow")]
public sealed class BillingItemWorkflow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("Description", "TariffVersions", type: "nvarchar(1000)",
            maxLength: 1000, nullable: true);
        migrationBuilder.AddColumn<int>("DisplayOrder", "TariffVersions", type: "int",
            nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<bool>("IsOptional", "TariffVersions", type: "bit",
            nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<bool>("ShowOnInvoice", "TariffVersions", type: "bit",
            nullable: false, defaultValue: true);
        migrationBuilder.AddColumn<bool>("IsManualOverride", "Charges", type: "bit",
            nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<string>("Note", "Charges", type: "nvarchar(1000)",
            maxLength: 1000, nullable: true);
        migrationBuilder.AddColumn<string>("Status", "Payments", type: "nvarchar(20)",
            maxLength: 20, nullable: false, defaultValue: "Posted");
        migrationBuilder.AddColumn<DateOnly>("ReversedOn", "Payments", type: "date", nullable: true);
        migrationBuilder.AddColumn<Guid>("ImportedByUserId", "BankStatementImports",
            type: "uniqueidentifier", nullable: true);
        migrationBuilder.AddCheckConstraint("CK_TariffVersions_DisplayOrder", "TariffVersions",
            "[DisplayOrder] >= 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("CK_TariffVersions_DisplayOrder", "TariffVersions");
        migrationBuilder.DropColumn("Description", "TariffVersions");
        migrationBuilder.DropColumn("DisplayOrder", "TariffVersions");
        migrationBuilder.DropColumn("IsOptional", "TariffVersions");
        migrationBuilder.DropColumn("ShowOnInvoice", "TariffVersions");
        migrationBuilder.DropColumn("IsManualOverride", "Charges");
        migrationBuilder.DropColumn("Note", "Charges");
        migrationBuilder.DropColumn("Status", "Payments");
        migrationBuilder.DropColumn("ReversedOn", "Payments");
        migrationBuilder.DropColumn("ImportedByUserId", "BankStatementImports");
    }
}
