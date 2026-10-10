using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zev.Api.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ZevDbContext))]
    [Migration("20261010191157_BankStatementBalances")]
    public partial class BankStatementBalances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ClosingBalance",
                table: "BankStatementImports",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OpeningBalance",
                table: "BankStatementImports",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "StatementFrom",
                table: "BankStatementImports",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "StatementTo",
                table: "BankStatementImports",
                type: "date",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_BankStatementImports_Period",
                table: "BankStatementImports",
                sql: "[StatementFrom] IS NULL OR [StatementTo] IS NULL OR [StatementTo] >= [StatementFrom]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_BankStatementImports_Period",
                table: "BankStatementImports");

            migrationBuilder.DropColumn(
                name: "ClosingBalance",
                table: "BankStatementImports");

            migrationBuilder.DropColumn(
                name: "OpeningBalance",
                table: "BankStatementImports");

            migrationBuilder.DropColumn(
                name: "StatementFrom",
                table: "BankStatementImports");

            migrationBuilder.DropColumn(
                name: "StatementTo",
                table: "BankStatementImports");
        }
    }
}
