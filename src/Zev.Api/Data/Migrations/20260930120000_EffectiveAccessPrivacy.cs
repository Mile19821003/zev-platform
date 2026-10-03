using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Zev.Api.Data.Migrations;

[DbContext(typeof(ZevDbContext))]
[Migration("20260930120000_EffectiveAccessPrivacy")]
public sealed class EffectiveAccessPrivacy : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("UserLinkStatus", "People", type: "nvarchar(20)",
            maxLength: 20, nullable: false, defaultValue: "Pending");
        migrationBuilder.AddColumn<Guid>("UserLinkReviewedByUserId", "People", type: "uniqueidentifier", nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>("UserLinkReviewedUtc", "People", type: "datetimeoffset", nullable: true);
        migrationBuilder.DropIndex("IX_People_UserId", "People");
        migrationBuilder.CreateIndex("IX_People_UserId_UserLinkStatus", "People",
            columns: ["UserId", "UserLinkStatus"]);

        migrationBuilder.AddColumn<Guid>("PropertyUnitId", "ManagedDocuments", type: "uniqueidentifier", nullable: true);
        migrationBuilder.AddColumn<string>("TargetRole", "ManagedDocuments", type: "nvarchar(40)",
            maxLength: 40, nullable: true);
        migrationBuilder.CreateIndex("IX_ManagedDocuments_OrganizationId_ZevId_PropertyUnitId", "ManagedDocuments",
            columns: ["OrganizationId", "ZevId", "PropertyUnitId"]);
        migrationBuilder.AddForeignKey("FK_ManagedDocuments_PropertyUnits_OrganizationId_ZevId_PropertyUnitId",
            table: "ManagedDocuments", columns: ["OrganizationId", "ZevId", "PropertyUnitId"],
            principalTable: "PropertyUnits", principalColumns: ["OrganizationId", "ZevId", "Id"],
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.CreateTable("AccessOverrides", table => new
        {
            Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            ZevId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            PermissionKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
            Effect = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
            ValidFromUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            ValidToUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
            Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
            CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            RevokedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
            RevokedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_AccessOverrides", item => item.Id);
            table.CheckConstraint("CK_AccessOverrides_Dates", "[ValidToUtc] IS NULL OR [ValidToUtc] > [ValidFromUtc]");
            table.ForeignKey("FK_AccessOverrides_Users_UserId", item => item.UserId, "Users", "Id",
                onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_AccessOverrides_Users_CreatedByUserId", item => item.CreatedByUserId, "Users", "Id",
                onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_AccessOverrides_Users_RevokedByUserId", item => item.RevokedByUserId, "Users", "Id",
                onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_AccessOverrides_Zevs_OrganizationId_ZevId", item => new { item.OrganizationId, item.ZevId },
                "Zevs", ["OrganizationId", "Id"], onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("IX_AccessOverrides_UserId_ZevId_PermissionKey_ValidFromUtc", "AccessOverrides",
            columns: ["UserId", "ZevId", "PermissionKey", "ValidFromUtc"]);
        migrationBuilder.CreateIndex("IX_AccessOverrides_CreatedByUserId", "AccessOverrides", "CreatedByUserId");
        migrationBuilder.CreateIndex("IX_AccessOverrides_RevokedByUserId", "AccessOverrides", "RevokedByUserId");
        migrationBuilder.CreateIndex("IX_AccessOverrides_OrganizationId_ZevId", "AccessOverrides",
            columns: ["OrganizationId", "ZevId"]);

        migrationBuilder.Sql("UPDATE [Memberships] SET [Role] = 'OrganizationAdministrator' WHERE [Role] = 'Manager' AND [ZevId] IS NULL");
        migrationBuilder.Sql("DELETE FROM [RolePermissions] WHERE [Role] = 'Owner' AND [PermissionKey] = 'zev.read'");
        migrationBuilder.Sql("DELETE FROM [RolePermissions] WHERE [Role] = 'Manager' AND [PermissionKey] = 'organization.read'");
        migrationBuilder.Sql("INSERT INTO [Permissions] ([Key], [Description]) VALUES ('finance.manage', 'Manage ZEV finances'), ('legal.manage', 'Manage ZEV registry and legal cases'), ('maintenance.manage', 'Manage ZEV maintenance')");
        migrationBuilder.Sql("INSERT INTO [RolePermissions] ([Role], [PermissionKey]) VALUES ('OrganizationAdministrator', 'zev.read'), ('OrganizationAdministrator', 'organization.read')");
        migrationBuilder.Sql("INSERT INTO [RolePermissions] ([Role], [PermissionKey]) VALUES ('OrganizationAdministrator', 'finance.manage'), ('OrganizationAdministrator', 'legal.manage'), ('OrganizationAdministrator', 'maintenance.manage'), ('Manager', 'finance.manage'), ('Manager', 'legal.manage'), ('Manager', 'maintenance.manage'), ('Accountant', 'finance.manage'), ('BuildingRepresentative', 'maintenance.manage')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("Rollback would erase verification and override history; restore a reviewed backup instead.");
    }
}
