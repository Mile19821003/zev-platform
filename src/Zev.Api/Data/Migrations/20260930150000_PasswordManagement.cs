using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Zev.Api.Data.Migrations;

[DbContext(typeof(ZevDbContext))]
[Migration("20260930150000_PasswordManagement")]
public sealed class PasswordManagement : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>("MustChangePassword", "Users", type: "bit",
            nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<int>("SecurityVersion", "Users", type: "int",
            nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>("SecurityVersion", "RefreshSessions", type: "int",
            nullable: false, defaultValue: 0);
        migrationBuilder.AlterColumn<string>("Action", "AuditLogs", type: "nvarchar(40)",
            maxLength: 40, nullable: false, oldClrType: typeof(string),
            oldType: "nvarchar(20)", oldMaxLength: 20);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("Rollback could reactivate revoked access tokens; restore a reviewed backup instead.");
    }
}
