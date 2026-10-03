using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zev.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase4Banking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_Charges_OrganizationId_ZevId_Id",
                table: "Charges",
                columns: new[] { "OrganizationId", "ZevId", "Id" });

            migrationBuilder.CreateTable(
                name: "BankAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZevId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FundId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    BankName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankAccounts", x => x.Id);
                    table.UniqueConstraint("AK_BankAccounts_OrganizationId_ZevId_Id", x => new { x.OrganizationId, x.ZevId, x.Id });
                    table.ForeignKey(
                        name: "FK_BankAccounts_Funds_OrganizationId_ZevId_FundId",
                        columns: x => new { x.OrganizationId, x.ZevId, x.FundId },
                        principalTable: "Funds",
                        principalColumns: new[] { "OrganizationId", "ZevId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankStatementImports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZevId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BankAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ChecksumSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Format = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TransactionCount = table.Column<int>(type: "int", nullable: false),
                    ImportedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatementImports", x => x.Id);
                    table.UniqueConstraint("AK_BankStatementImports_OrganizationId_ZevId_Id", x => new { x.OrganizationId, x.ZevId, x.Id });
                    table.ForeignKey(
                        name: "FK_BankStatementImports_BankAccounts_OrganizationId_ZevId_BankAccountId",
                        columns: x => new { x.OrganizationId, x.ZevId, x.BankAccountId },
                        principalTable: "BankAccounts",
                        principalColumns: new[] { "OrganizationId", "ZevId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZevId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    BookingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Counterparty = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankTransactions", x => x.Id);
                    table.UniqueConstraint("AK_BankTransactions_OrganizationId_ZevId_Id", x => new { x.OrganizationId, x.ZevId, x.Id });
                    table.CheckConstraint("CK_BankTransactions_Amount", "[Amount] <> 0");
                    table.ForeignKey(
                        name: "FK_BankTransactions_BankStatementImports_OrganizationId_ZevId_ImportId",
                        columns: x => new { x.OrganizationId, x.ZevId, x.ImportId },
                        principalTable: "BankStatementImports",
                        principalColumns: new[] { "OrganizationId", "ZevId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZevId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FundId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BankTransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PaidOn = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.UniqueConstraint("AK_Payments_OrganizationId_ZevId_Id", x => new { x.OrganizationId, x.ZevId, x.Id });
                    table.CheckConstraint("CK_Payments_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_Payments_BankTransactions_OrganizationId_ZevId_BankTransactionId",
                        columns: x => new { x.OrganizationId, x.ZevId, x.BankTransactionId },
                        principalTable: "BankTransactions",
                        principalColumns: new[] { "OrganizationId", "ZevId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_People_OrganizationId_PersonId",
                        columns: x => new { x.OrganizationId, x.PersonId },
                        principalTable: "People",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReconciliationAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZevId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BankTransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Rule = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Result = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciliationAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReconciliationAttempts_BankTransactions_OrganizationId_ZevId_BankTransactionId",
                        columns: x => new { x.OrganizationId, x.ZevId, x.BankTransactionId },
                        principalTable: "BankTransactions",
                        principalColumns: new[] { "OrganizationId", "ZevId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZevId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChargeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentAllocations", x => x.Id);
                    table.CheckConstraint("CK_PaymentAllocations_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_PaymentAllocations_Charges_OrganizationId_ZevId_ChargeId",
                        columns: x => new { x.OrganizationId, x.ZevId, x.ChargeId },
                        principalTable: "Charges",
                        principalColumns: new[] { "OrganizationId", "ZevId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentAllocations_Payments_OrganizationId_ZevId_PaymentId",
                        columns: x => new { x.OrganizationId, x.ZevId, x.PaymentId },
                        principalTable: "Payments",
                        principalColumns: new[] { "OrganizationId", "ZevId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankAccounts_OrganizationId_ZevId_FundId",
                table: "BankAccounts",
                columns: new[] { "OrganizationId", "ZevId", "FundId" });

            migrationBuilder.CreateIndex(
                name: "IX_BankAccounts_ZevId_AccountNumber",
                table: "BankAccounts",
                columns: new[] { "ZevId", "AccountNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementImports_OrganizationId_ZevId_BankAccountId",
                table: "BankStatementImports",
                columns: new[] { "OrganizationId", "ZevId", "BankAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementImports_ZevId_BankAccountId_ChecksumSha256",
                table: "BankStatementImports",
                columns: new[] { "ZevId", "BankAccountId", "ChecksumSha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementImports_ZevId_ImportedUtc",
                table: "BankStatementImports",
                columns: new[] { "ZevId", "ImportedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BankTransactions_ImportId_Sequence",
                table: "BankTransactions",
                columns: new[] { "ImportId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankTransactions_OrganizationId_ZevId_ImportId",
                table: "BankTransactions",
                columns: new[] { "OrganizationId", "ZevId", "ImportId" });

            migrationBuilder.CreateIndex(
                name: "IX_BankTransactions_ZevId_Reference",
                table: "BankTransactions",
                columns: new[] { "ZevId", "Reference" });

            migrationBuilder.CreateIndex(
                name: "IX_BankTransactions_ZevId_Status_BookingDate",
                table: "BankTransactions",
                columns: new[] { "ZevId", "Status", "BookingDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocations_ChargeId",
                table: "PaymentAllocations",
                column: "ChargeId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocations_OrganizationId_ZevId_ChargeId",
                table: "PaymentAllocations",
                columns: new[] { "OrganizationId", "ZevId", "ChargeId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocations_OrganizationId_ZevId_PaymentId",
                table: "PaymentAllocations",
                columns: new[] { "OrganizationId", "ZevId", "PaymentId" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocations_PaymentId_ChargeId",
                table: "PaymentAllocations",
                columns: new[] { "PaymentId", "ChargeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_BankTransactionId",
                table: "Payments",
                column: "BankTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_OrganizationId_PersonId",
                table: "Payments",
                columns: new[] { "OrganizationId", "PersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_OrganizationId_ZevId_BankTransactionId",
                table: "Payments",
                columns: new[] { "OrganizationId", "ZevId", "BankTransactionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ZevId_PersonId_PaidOn",
                table: "Payments",
                columns: new[] { "ZevId", "PersonId", "PaidOn" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationAttempts_BankTransactionId_CreatedUtc",
                table: "ReconciliationAttempts",
                columns: new[] { "BankTransactionId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationAttempts_OrganizationId_ZevId_BankTransactionId",
                table: "ReconciliationAttempts",
                columns: new[] { "OrganizationId", "ZevId", "BankTransactionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentAllocations");

            migrationBuilder.DropTable(
                name: "ReconciliationAttempts");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropTable(
                name: "BankTransactions");

            migrationBuilder.DropTable(
                name: "BankStatementImports");

            migrationBuilder.DropTable(
                name: "BankAccounts");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Charges_OrganizationId_ZevId_Id",
                table: "Charges");
        }
    }
}
