using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Zev.Api.Data.Migrations;

[DbContext(typeof(ZevDbContext))]
[Migration("20261003100000_RegistryListIndex")]
public sealed class RegistryListIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateIndex(
            name: "IX_PropertyUnits_ZevId_EntranceId_Code_Id",
            table: "PropertyUnits",
            columns: new[] { "ZevId", "EntranceId", "Code", "Id" });

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropIndex(
            name: "IX_PropertyUnits_ZevId_EntranceId_Code_Id",
            table: "PropertyUnits");
}
