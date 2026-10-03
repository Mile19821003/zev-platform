using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Zev.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase7CommunityVoting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Announcements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZevId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PublishedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    BuildingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EntranceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TargetRole = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", maxLength: 10000, nullable: false),
                    PublishedFromUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Announcements", x => x.Id);
                    table.UniqueConstraint("AK_Announcements_OrganizationId_ZevId_Id", x => new { x.OrganizationId, x.ZevId, x.Id });
                    table.CheckConstraint("CK_Announcements_Dates", "[ExpiresUtc] IS NULL OR [ExpiresUtc] > [PublishedFromUtc]");
                    table.ForeignKey(
                        name: "FK_Announcements_Buildings_OrganizationId_ZevId_BuildingId",
                        columns: x => new { x.OrganizationId, x.ZevId, x.BuildingId },
                        principalTable: "Buildings",
                        principalColumns: new[] { "OrganizationId", "ZevId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Announcements_Entrances_OrganizationId_ZevId_BuildingId_EntranceId",
                        columns: x => new { x.OrganizationId, x.ZevId, x.BuildingId, x.EntranceId },
                        principalTable: "Entrances",
                        principalColumns: new[] { "OrganizationId", "ZevId", "BuildingId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Announcements_Zevs_OrganizationId_ZevId",
                        columns: x => new { x.OrganizationId, x.ZevId },
                        principalTable: "Zevs",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Ballots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZevId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Question = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    WeightBasis = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ApprovalDenominator = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    QuorumPercent = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: false),
                    ApprovalPercent = table.Column<decimal>(type: "decimal(7,4)", precision: 7, scale: 4, nullable: false),
                    OpensUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ClosesUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EligibleWeight = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    CastWeight = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    YesWeight = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    NoWeight = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    AbstainWeight = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    QuorumMet = table.Column<bool>(type: "bit", nullable: true),
                    Approved = table.Column<bool>(type: "bit", nullable: true),
                    ClosedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ballots", x => x.Id);
                    table.UniqueConstraint("AK_Ballots_OrganizationId_ZevId_Id", x => new { x.OrganizationId, x.ZevId, x.Id });
                    table.CheckConstraint("CK_Ballots_Approval", "[ApprovalPercent] >= 0 AND [ApprovalPercent] <= 100");
                    table.CheckConstraint("CK_Ballots_Dates", "[ClosesUtc] > [OpensUtc]");
                    table.CheckConstraint("CK_Ballots_Quorum", "[QuorumPercent] >= 0 AND [QuorumPercent] <= 100");
                    table.ForeignKey(
                        name: "FK_Ballots_Zevs_OrganizationId_ZevId",
                        columns: x => new { x.OrganizationId, x.ZevId },
                        principalTable: "Zevs",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ManagedDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZevId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoredFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Visibility = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManagedDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ManagedDocuments_People_OrganizationId_PersonId",
                        columns: x => new { x.OrganizationId, x.PersonId },
                        principalTable: "People",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ManagedDocuments_StoredFiles_OrganizationId_ZevId_StoredFileId",
                        columns: x => new { x.OrganizationId, x.ZevId, x.StoredFileId },
                        principalTable: "StoredFiles",
                        principalColumns: new[] { "OrganizationId", "ZevId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AnnouncementFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZevId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AnnouncementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoredFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnnouncementFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnnouncementFiles_Announcements_OrganizationId_ZevId_AnnouncementId",
                        columns: x => new { x.OrganizationId, x.ZevId, x.AnnouncementId },
                        principalTable: "Announcements",
                        principalColumns: new[] { "OrganizationId", "ZevId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AnnouncementFiles_StoredFiles_OrganizationId_ZevId_StoredFileId",
                        columns: x => new { x.OrganizationId, x.ZevId, x.StoredFileId },
                        principalTable: "StoredFiles",
                        principalColumns: new[] { "OrganizationId", "ZevId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BallotEligibilities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZevId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BallotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PropertyUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Weight = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BallotEligibilities", x => x.Id);
                    table.CheckConstraint("CK_BallotEligibilities_Weight", "[Weight] > 0");
                    table.ForeignKey(
                        name: "FK_BallotEligibilities_Ballots_OrganizationId_ZevId_BallotId",
                        columns: x => new { x.OrganizationId, x.ZevId, x.BallotId },
                        principalTable: "Ballots",
                        principalColumns: new[] { "OrganizationId", "ZevId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BallotEligibilities_People_OrganizationId_PersonId",
                        columns: x => new { x.OrganizationId, x.PersonId },
                        principalTable: "People",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BallotEligibilities_PropertyUnits_OrganizationId_ZevId_PropertyUnitId",
                        columns: x => new { x.OrganizationId, x.ZevId, x.PropertyUnitId },
                        principalTable: "PropertyUnits",
                        principalColumns: new[] { "OrganizationId", "ZevId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Votes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ZevId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BallotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PropertyUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CastByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Choice = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    WeightSnapshot = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CastUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Votes", x => x.Id);
                    table.CheckConstraint("CK_Votes_Weight", "[WeightSnapshot] > 0");
                    table.ForeignKey(
                        name: "FK_Votes_Ballots_OrganizationId_ZevId_BallotId",
                        columns: x => new { x.OrganizationId, x.ZevId, x.BallotId },
                        principalTable: "Ballots",
                        principalColumns: new[] { "OrganizationId", "ZevId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Votes_People_OrganizationId_PersonId",
                        columns: x => new { x.OrganizationId, x.PersonId },
                        principalTable: "People",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Votes_PropertyUnits_OrganizationId_ZevId_PropertyUnitId",
                        columns: x => new { x.OrganizationId, x.ZevId, x.PropertyUnitId },
                        principalTable: "PropertyUnits",
                        principalColumns: new[] { "OrganizationId", "ZevId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementFiles_AnnouncementId_StoredFileId",
                table: "AnnouncementFiles",
                columns: new[] { "AnnouncementId", "StoredFileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementFiles_OrganizationId_ZevId_AnnouncementId",
                table: "AnnouncementFiles",
                columns: new[] { "OrganizationId", "ZevId", "AnnouncementId" });

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementFiles_OrganizationId_ZevId_StoredFileId",
                table: "AnnouncementFiles",
                columns: new[] { "OrganizationId", "ZevId", "StoredFileId" });

            migrationBuilder.CreateIndex(
                name: "IX_Announcements_OrganizationId_ZevId_BuildingId_EntranceId",
                table: "Announcements",
                columns: new[] { "OrganizationId", "ZevId", "BuildingId", "EntranceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Announcements_ZevId_Scope_PublishedFromUtc",
                table: "Announcements",
                columns: new[] { "ZevId", "Scope", "PublishedFromUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BallotEligibilities_BallotId_PropertyUnitId_PersonId",
                table: "BallotEligibilities",
                columns: new[] { "BallotId", "PropertyUnitId", "PersonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BallotEligibilities_OrganizationId_PersonId",
                table: "BallotEligibilities",
                columns: new[] { "OrganizationId", "PersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_BallotEligibilities_OrganizationId_ZevId_BallotId",
                table: "BallotEligibilities",
                columns: new[] { "OrganizationId", "ZevId", "BallotId" });

            migrationBuilder.CreateIndex(
                name: "IX_BallotEligibilities_OrganizationId_ZevId_PropertyUnitId",
                table: "BallotEligibilities",
                columns: new[] { "OrganizationId", "ZevId", "PropertyUnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_BallotEligibilities_PersonId_BallotId",
                table: "BallotEligibilities",
                columns: new[] { "PersonId", "BallotId" });

            migrationBuilder.CreateIndex(
                name: "IX_Ballots_ZevId_Status_OpensUtc",
                table: "Ballots",
                columns: new[] { "ZevId", "Status", "OpensUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ManagedDocuments_OrganizationId_PersonId",
                table: "ManagedDocuments",
                columns: new[] { "OrganizationId", "PersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_ManagedDocuments_OrganizationId_ZevId_StoredFileId",
                table: "ManagedDocuments",
                columns: new[] { "OrganizationId", "ZevId", "StoredFileId" });

            migrationBuilder.CreateIndex(
                name: "IX_ManagedDocuments_ZevId_Visibility_Type",
                table: "ManagedDocuments",
                columns: new[] { "ZevId", "Visibility", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_Votes_BallotId_PropertyUnitId",
                table: "Votes",
                columns: new[] { "BallotId", "PropertyUnitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Votes_OrganizationId_PersonId",
                table: "Votes",
                columns: new[] { "OrganizationId", "PersonId" });

            migrationBuilder.CreateIndex(
                name: "IX_Votes_OrganizationId_ZevId_BallotId",
                table: "Votes",
                columns: new[] { "OrganizationId", "ZevId", "BallotId" });

            migrationBuilder.CreateIndex(
                name: "IX_Votes_OrganizationId_ZevId_PropertyUnitId",
                table: "Votes",
                columns: new[] { "OrganizationId", "ZevId", "PropertyUnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_Votes_PersonId_BallotId",
                table: "Votes",
                columns: new[] { "PersonId", "BallotId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnnouncementFiles");

            migrationBuilder.DropTable(
                name: "BallotEligibilities");

            migrationBuilder.DropTable(
                name: "ManagedDocuments");

            migrationBuilder.DropTable(
                name: "Votes");

            migrationBuilder.DropTable(
                name: "Announcements");

            migrationBuilder.DropTable(
                name: "Ballots");
        }
    }
}
