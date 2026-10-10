using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zev.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ZevDbContext))]
    [Migration("20261010182929_AccessOverrideRevokeReason")]
    public partial class AccessOverrideRevokeReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RevokeReason",
                table: "AccessOverrides",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_AccessOverrides_RevokeReason",
                table: "AccessOverrides",
                sql: "[RevokeReason] IS NULL OR [RevokedUtc] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AccessOverrides_RevokeReason",
                table: "AccessOverrides");

            migrationBuilder.DropColumn(
                name: "RevokeReason",
                table: "AccessOverrides");
        }
    }
}
