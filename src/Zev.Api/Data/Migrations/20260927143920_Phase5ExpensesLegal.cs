using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zev.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase5ExpensesLegal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InterestRatePolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZevId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidTo = table.Column<DateOnly>(type: "date", nullable: true),
                    AnnualRatePercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    DaysPerYear = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InterestRatePolicies", x => x.Id);
                    table.CheckConstraint("CK_InterestRatePolicies_Dates", "[ValidTo] IS NULL OR [ValidTo] > [ValidFrom]");
                    table.CheckConstraint("CK_InterestRatePolicies_Days", "[DaysPerYear] > 0");
                    table.CheckConstraint("CK_InterestRatePolicies_Rate", "[AnnualRatePercent] >= 0");
                    table.ForeignKey(
                        name: "FK_InterestRatePolicies_Zevs_OrganizationId_ZevId",
                        columns: x => new { x.OrganizationId, x.ZevId },
                        principalTable: "Zevs",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LegalCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZevId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PrincipalAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    InterestAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CostsAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RecoveredAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Lawyer = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Court = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CaseNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    FiledOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegalCases", x => x.Id);
                    table.UniqueConstraint("AK_LegalCases_OrganizationId_ZevId_Id", x => new { x.OrganizationId, x.ZevId, x.Id });
                    table.CheckConstraint("CK_LegalCases_Costs", "[CostsAmount] >= 0");
                    table.CheckConstraint("CK_LegalCases_Interest", "[InterestAmount] >= 0");
                    table.CheckConstraint("CK_LegalCases_Principal", "[PrincipalAmount] >= 0");
                    table.CheckConstraint("CK_LegalCases_Recovered", "[RecoveredAmount] >= 0");
                    table.ForeignKey(
                        name: "FK_LegalCases_People_OrganizationId_PersonId",
                        columns: x => new { x.OrganizationId, x.PersonId },
                        principalTable: "People",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LegalCases_Zevs_OrganizationId_ZevId",
                        columns: x => new { x.OrganizationId, x.ZevId },
                        principalTable: "Zevs",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Reminders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZevId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PrincipalSnapshot = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    InterestSnapshot = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AsOf = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reminders", x => x.Id);
                    table.CheckConstraint("CK_Reminders_Interest", "[InterestSnapshot] >= 0");
                    table.CheckConstraint("CK_Reminders_Principal", "[PrincipalSnapshot] >= 0");
                    table.ForeignKey(
                        name: "FK_Reminders_People_OrganizationId_PersonId",
                        columns: x => new { x.OrganizationId, x.PersonId },
                        principalTable: "People",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Reminders_Zevs_OrganizationId_ZevId",
                        columns: x => new { x.OrganizationId, x.ZevId },
                        principalTable: "Zevs",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StoredFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZevId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Length = table.Column<long>(type: "bigint", nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UploadedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredFiles", x => x.Id);
                    table.UniqueConstraint("AK_StoredFiles_OrganizationId_ZevId_Id", x => new { x.OrganizationId, x.ZevId, x.Id });
                    table.CheckConstraint("CK_StoredFiles_Length", "[Length] > 0");
                    table.ForeignKey(
                        name: "FK_StoredFiles_Zevs_OrganizationId_ZevId",
                        columns: x => new { x.OrganizationId, x.ZevId },
                        principalTable: "Zevs",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Suppliers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TaxIdentifier = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Suppliers", x => x.Id);
                    table.UniqueConstraint("AK_Suppliers_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_Suppliers_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LegalCaseFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZevId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LegalCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoredFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegalCaseFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LegalCaseFiles_LegalCases_OrganizationId_ZevId_LegalCaseId",
                        columns: x => new { x.OrganizationId, x.ZevId, x.LegalCaseId },
                        principalTable: "LegalCases",
                        principalColumns: new[] { "OrganizationId", "ZevId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LegalCaseFiles_StoredFiles_OrganizationId_ZevId_StoredFileId",
                        columns: x => new { x.OrganizationId, x.ZevId, x.StoredFileId },
                        principalTable: "StoredFiles",
                        principalColumns: new[] { "OrganizationId", "ZevId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Expenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZevId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FundId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    InvoiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Expenses", x => x.Id);
                    table.CheckConstraint("CK_Expenses_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_Expenses_Funds_OrganizationId_ZevId_FundId",
                        columns: x => new { x.OrganizationId, x.ZevId, x.FundId },
                        principalTable: "Funds",
                        principalColumns: new[] { "OrganizationId", "ZevId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Expenses_StoredFiles_OrganizationId_ZevId_DocumentId",
                        columns: x => new { x.OrganizationId, x.ZevId, x.DocumentId },
                        principalTable: "StoredFiles",
                        principalColumns: new[] { "OrganizationId", "ZevId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Expenses_Suppliers_OrganizationId_SupplierId",
                        columns: x => new { x.OrganizationId, x.SupplierId },
                        principalTable: "Suppliers",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FundTransactions_Kind_SourceId",
                table: "FundTransactions",
                columns: new[] { "Kind", "SourceId" },
                unique: true,
                filter: "[SourceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_OrganizationId_SupplierId",
                table: "Expenses",
                columns: new[] { "OrganizationId", "SupplierId" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_OrganizationId_ZevId_DocumentId",
                table: "Expenses",
                columns: new[] { "OrganizationId", "ZevId", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_OrganizationId_ZevId_FundId",
                table: "Expenses",
                columns: new[] { "OrganizationId", "ZevId", "FundId" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_SupplierId_InvoiceNumber",
                table: "Expenses",
                columns: new[] { "SupplierId", "InvoiceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_ZevId_InvoiceDate_Status",
                table: "Expenses",
                columns: new[] { "ZevId", "InvoiceDate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_InterestRatePolicies_OrganizationId_ZevId",
                table: "InterestRatePolicies",
                columns: new[] { "OrganizationId", "ZevId" });

            migrationBuilder.CreateIndex(
                name: "IX_InterestRatePolicies_ZevId_ValidFrom",
                table: "InterestRatePolicies",
                columns: new[] { "ZevId", "ValidFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_LegalCaseFiles_LegalCaseId_StoredFileId",
                table: "LegalCaseFiles",
                columns: new[] { "LegalCaseId", "StoredFileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LegalCaseFiles_OrganizationId_ZevId_LegalCaseId",
                table: "LegalCaseFiles",
                columns: new[] { "OrganizationId", "ZevId", "LegalCaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_LegalCaseFiles_OrganizationId_ZevId_StoredFileId",
                table: "LegalCaseFiles",
                columns: new[] { "OrganizationId", "ZevId", "StoredFileId" });

            migrationBuilder.CreateIndex(
                name: "IX_LegalCases_OrganizationId_PersonId",
                table: "LegalCases",
                columns: new[] { "OrganizationId", "PersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_LegalCases_ZevId_Status_FiledOn",
                table: "LegalCases",
                columns: new[] { "ZevId", "Status", "FiledOn" });

            migrationBuilder.CreateIndex(
                name: "IX_Reminders_OrganizationId_PersonId",
                table: "Reminders",
                columns: new[] { "OrganizationId", "PersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_Reminders_OrganizationId_ZevId",
                table: "Reminders",
                columns: new[] { "OrganizationId", "ZevId" });

            migrationBuilder.CreateIndex(
                name: "IX_Reminders_ZevId_PersonId_AsOf",
                table: "Reminders",
                columns: new[] { "ZevId", "PersonId", "AsOf" });

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_ZevId_UploadedUtc",
                table: "StoredFiles",
                columns: new[] { "ZevId", "UploadedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_OrganizationId_Name",
                table: "Suppliers",
                columns: new[] { "OrganizationId", "Name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Expenses");

            migrationBuilder.DropTable(
                name: "InterestRatePolicies");

            migrationBuilder.DropTable(
                name: "LegalCaseFiles");

            migrationBuilder.DropTable(
                name: "Reminders");

            migrationBuilder.DropTable(
                name: "Suppliers");

            migrationBuilder.DropTable(
                name: "LegalCases");

            migrationBuilder.DropTable(
                name: "StoredFiles");

            migrationBuilder.DropIndex(
                name: "IX_FundTransactions_Kind_SourceId",
                table: "FundTransactions");
        }
    }
}
