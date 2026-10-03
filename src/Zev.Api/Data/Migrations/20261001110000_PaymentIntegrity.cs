using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Zev.Api.Data.Migrations;

[DbContext(typeof(ZevDbContext))]
[Migration("20261001110000_PaymentIntegrity")]
public sealed class PaymentIntegrity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PaymentIdentificationHistories",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ZevId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PreviousPersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                NewPersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                ChangedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ChangedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PaymentIdentificationHistories", x => x.Id);
                table.CheckConstraint("CK_PaymentIdentificationHistories_Reason", "LEN(LTRIM(RTRIM([Reason]))) > 0");
                table.ForeignKey("FK_PaymentIdentificationHistories_Payments_OrganizationId_ZevId_PaymentId",
                    x => new { x.OrganizationId, x.ZevId, x.PaymentId }, "Payments",
                    new[] { "OrganizationId", "ZevId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_PaymentIdentificationHistories_People_OrganizationId_PreviousPersonId",
                    x => new { x.OrganizationId, x.PreviousPersonId }, "People",
                    new[] { "OrganizationId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_PaymentIdentificationHistories_People_OrganizationId_NewPersonId",
                    x => new { x.OrganizationId, x.NewPersonId }, "People",
                    new[] { "OrganizationId", "Id" }, onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_PaymentIdentificationHistories_OrganizationId_PreviousPersonId",
            "PaymentIdentificationHistories", new[] { "OrganizationId", "PreviousPersonId" });
        migrationBuilder.CreateIndex("IX_PaymentIdentificationHistories_OrganizationId_NewPersonId",
            "PaymentIdentificationHistories", new[] { "OrganizationId", "NewPersonId" });
        migrationBuilder.CreateIndex("IX_PaymentIdentificationHistories_OrganizationId_ZevId_PaymentId",
            "PaymentIdentificationHistories", new[] { "OrganizationId", "ZevId", "PaymentId" });
        migrationBuilder.CreateIndex("IX_PaymentIdentificationHistories_PaymentId_ChangedUtc",
            "PaymentIdentificationHistories", new[] { "PaymentId", "ChangedUtc" });
        migrationBuilder.CreateIndex("IX_PaymentIdentificationHistories_ZevId_PreviousPersonId_ChangedUtc",
            "PaymentIdentificationHistories", new[] { "ZevId", "PreviousPersonId", "ChangedUtc" });
        migrationBuilder.CreateIndex("IX_PaymentIdentificationHistories_ZevId_NewPersonId_ChangedUtc",
            "PaymentIdentificationHistories", new[] { "ZevId", "NewPersonId", "ChangedUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new InvalidOperationException("Payment identification history cannot be dropped by rollback.");
}
