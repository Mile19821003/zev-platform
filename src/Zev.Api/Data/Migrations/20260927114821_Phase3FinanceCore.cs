using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zev.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase3FinanceCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BillingRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZevId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Period = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ChargeCount = table.Column<int>(type: "int", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingRuns", x => x.Id);
                    table.UniqueConstraint("AK_BillingRuns_OrganizationId_ZevId_Id", x => new { x.OrganizationId, x.ZevId, x.Id });
                    table.CheckConstraint("CK_BillingRuns_Total", "[TotalAmount] >= 0");
                    table.ForeignKey(
                        name: "FK_BillingRuns_Zevs_OrganizationId_ZevId",
                        columns: x => new { x.OrganizationId, x.ZevId },
                        principalTable: "Zevs",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Funds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZevId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Funds", x => x.Id);
                    table.UniqueConstraint("AK_Funds_OrganizationId_ZevId_Id", x => new { x.OrganizationId, x.ZevId, x.Id });
                    table.ForeignKey(
                        name: "FK_Funds_Zevs_OrganizationId_ZevId",
                        columns: x => new { x.OrganizationId, x.ZevId },
                        principalTable: "Zevs",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FundTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZevId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FundId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SignedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReversesTransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EffectiveOn = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundTransactions", x => x.Id);
                    table.CheckConstraint("CK_FundTransactions_Nonzero", "[SignedAmount] <> 0");
                    table.ForeignKey(
                        name: "FK_FundTransactions_FundTransactions_ReversesTransactionId",
                        column: x => x.ReversesTransactionId,
                        principalTable: "FundTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FundTransactions_Funds_OrganizationId_ZevId_FundId",
                        columns: x => new { x.OrganizationId, x.ZevId, x.FundId },
                        principalTable: "Funds",
                        principalColumns: new[] { "OrganizationId", "ZevId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TariffVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZevId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Basis = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    FundId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidTo = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TariffVersions", x => x.Id);
                    table.CheckConstraint("CK_TariffVersions_Dates", "[ValidTo] IS NULL OR [ValidTo] > [ValidFrom]");
                    table.CheckConstraint("CK_TariffVersions_Rate", "[Rate] >= 0");
                    table.ForeignKey(
                        name: "FK_TariffVersions_Funds_OrganizationId_ZevId_FundId",
                        columns: x => new { x.OrganizationId, x.ZevId, x.FundId },
                        principalTable: "Funds",
                        principalColumns: new[] { "OrganizationId", "ZevId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Charges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZevId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PropertyUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FundId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TariffVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BillingRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Period = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    BasisQuantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    RateSnapshot = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OwnershipShareSnapshot = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Charges", x => x.Id);
                    table.CheckConstraint("CK_Charges_Amount", "[Amount] >= 0");
                    table.ForeignKey(
                        name: "FK_Charges_BillingRuns_OrganizationId_ZevId_BillingRunId",
                        columns: x => new { x.OrganizationId, x.ZevId, x.BillingRunId },
                        principalTable: "BillingRuns",
                        principalColumns: new[] { "OrganizationId", "ZevId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Charges_Funds_OrganizationId_ZevId_FundId",
                        columns: x => new { x.OrganizationId, x.ZevId, x.FundId },
                        principalTable: "Funds",
                        principalColumns: new[] { "OrganizationId", "ZevId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Charges_People_OrganizationId_PersonId",
                        columns: x => new { x.OrganizationId, x.PersonId },
                        principalTable: "People",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Charges_PropertyUnits_OrganizationId_ZevId_PropertyUnitId",
                        columns: x => new { x.OrganizationId, x.ZevId, x.PropertyUnitId },
                        principalTable: "PropertyUnits",
                        principalColumns: new[] { "OrganizationId", "ZevId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Charges_TariffVersions_TariffVersionId",
                        column: x => x.TariffVersionId,
                        principalTable: "TariffVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillingRuns_ZevId_Period",
                table: "BillingRuns",
                columns: new[] { "ZevId", "Period" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Charges_OrganizationId_PersonId",
                table: "Charges",
                columns: new[] { "OrganizationId", "PersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_Charges_OrganizationId_ZevId_BillingRunId",
                table: "Charges",
                columns: new[] { "OrganizationId", "ZevId", "BillingRunId" });

            migrationBuilder.CreateIndex(
                name: "IX_Charges_OrganizationId_ZevId_FundId",
                table: "Charges",
                columns: new[] { "OrganizationId", "ZevId", "FundId" });

            migrationBuilder.CreateIndex(
                name: "IX_Charges_OrganizationId_ZevId_PropertyUnitId",
                table: "Charges",
                columns: new[] { "OrganizationId", "ZevId", "PropertyUnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_Charges_PersonId_DueDate",
                table: "Charges",
                columns: new[] { "PersonId", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Charges_Reference",
                table: "Charges",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Charges_TariffVersionId",
                table: "Charges",
                column: "TariffVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_Charges_ZevId_Period_PropertyUnitId_PersonId_TariffVersionId",
                table: "Charges",
                columns: new[] { "ZevId", "Period", "PropertyUnitId", "PersonId", "TariffVersionId" },
                unique: true,
                filter: "[BillingRunId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Funds_ZevId_Kind",
                table: "Funds",
                columns: new[] { "ZevId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FundTransactions_FundId_EffectiveOn",
                table: "FundTransactions",
                columns: new[] { "FundId", "EffectiveOn" });

            migrationBuilder.CreateIndex(
                name: "IX_FundTransactions_OrganizationId_ZevId_FundId",
                table: "FundTransactions",
                columns: new[] { "OrganizationId", "ZevId", "FundId" });

            migrationBuilder.CreateIndex(
                name: "IX_FundTransactions_ReversesTransactionId",
                table: "FundTransactions",
                column: "ReversesTransactionId",
                unique: true,
                filter: "[ReversesTransactionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TariffVersions_OrganizationId_ZevId_FundId",
                table: "TariffVersions",
                columns: new[] { "OrganizationId", "ZevId", "FundId" });

            migrationBuilder.CreateIndex(
                name: "IX_TariffVersions_ZevId_Code_Version",
                table: "TariffVersions",
                columns: new[] { "ZevId", "Code", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TariffVersions_ZevId_IsActive_ValidFrom_ValidTo",
                table: "TariffVersions",
                columns: new[] { "ZevId", "IsActive", "ValidFrom", "ValidTo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Charges");

            migrationBuilder.DropTable(
                name: "FundTransactions");

            migrationBuilder.DropTable(
                name: "BillingRuns");

            migrationBuilder.DropTable(
                name: "TariffVersions");

            migrationBuilder.DropTable(
                name: "Funds");
        }
    }
}
