using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zev.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ZevDbContext))]
    [Migration("20261010181657_ManualPaymentReversal")]
    public partial class ManualPaymentReversal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReversalReason",
                table: "Payments",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReversedByUserId",
                table: "Payments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_Reversal",
                table: "Payments",
                sql: "[ReversalReason] IS NULL OR ([Status] = 'Reversed' AND LEN(LTRIM(RTRIM([ReversalReason]))) > 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_Reversal",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ReversalReason",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ReversedByUserId",
                table: "Payments");
        }
    }
}
