using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaHandler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class KodiScannerSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "NfoMetadataId",
                table: "Medias",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Year",
                table: "Medias",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Fingerprint",
                table: "MediaFiles",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "FirstSeenScanRunId",
                table: "MediaFiles",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "LastSeenScanRunId",
                table: "MediaFiles",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LibraryRootId",
                table: "MediaFiles",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MissingSince",
                table: "MediaFiles",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MtimeUtc",
                table: "MediaFiles",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "MediaFiles",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "StackGroupId",
                table: "MediaFiles",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EpisodeFileLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TvEpisodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MediaFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderInFile = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EpisodeFileLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EpisodeFileLinks_MediaFiles_MediaFileId",
                        column: x => x.MediaFileId,
                        principalTable: "MediaFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EpisodeFileLinks_TvEpisodes_TvEpisodeId",
                        column: x => x.TvEpisodeId,
                        principalTable: "TvEpisodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExclusionRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Pattern = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RuleId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExclusionRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LibraryRoots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Path = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryRoots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NfoMetadata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourcePath = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    RawContent = table.Column<string>(type: "nvarchar(max)", maxLength: 32768, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Year = table.Column<int>(type: "int", nullable: true),
                    TmdbId = table.Column<int>(type: "int", nullable: true),
                    ImdbId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Season = table.Column<int>(type: "int", nullable: true),
                    Episode = table.Column<int>(type: "int", nullable: true),
                    ParseFailed = table.Column<bool>(type: "bit", nullable: false),
                    ParseError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NfoMetadata", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReviewItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ParsedTitle = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ParsedYear = table.Column<int>(type: "int", nullable: true),
                    ParsedSeason = table.Column<int>(type: "int", nullable: true),
                    ParsedEpisode = table.Column<int>(type: "int", nullable: true),
                    CandidatesJson = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "[]"),
                    ResolvedTmdbId = table.Column<int>(type: "int", nullable: true),
                    ResolvedKind = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolvedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FirstSeenScanRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScanRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    LibraryRootIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "[]"),
                    TotalDiscovered = table.Column<int>(type: "int", nullable: false),
                    Added = table.Column<int>(type: "int", nullable: false),
                    Updated = table.Column<int>(type: "int", nullable: false),
                    Unchanged = table.Column<int>(type: "int", nullable: false),
                    Removed = table.Column<int>(type: "int", nullable: false),
                    Excluded = table.Column<int>(type: "int", nullable: false),
                    NeedsReview = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StackGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MediaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MediaId1 = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StackGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StackGroups_Medias_MediaId",
                        column: x => x.MediaId,
                        principalTable: "Medias",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StackGroups_Medias_MediaId1",
                        column: x => x.MediaId1,
                        principalTable: "Medias",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ScanItemDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScanRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RuleId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MediaFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanItemDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScanItemDecisions_MediaFiles_MediaFileId",
                        column: x => x.MediaFileId,
                        principalTable: "MediaFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ScanItemDecisions_ReviewItems_ReviewItemId",
                        column: x => x.ReviewItemId,
                        principalTable: "ReviewItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ScanItemDecisions_ScanRuns_ScanRunId",
                        column: x => x.ScanRunId,
                        principalTable: "ScanRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Medias_NfoMetadataId",
                table: "Medias",
                column: "NfoMetadataId",
                unique: true,
                filter: "[NfoMetadataId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MediaFiles_LibraryRootId",
                table: "MediaFiles",
                column: "LibraryRootId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaFiles_LibraryRootId_Fingerprint",
                table: "MediaFiles",
                columns: new[] { "LibraryRootId", "Fingerprint" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaFiles_MissingSince",
                table: "MediaFiles",
                column: "MissingSince");

            migrationBuilder.CreateIndex(
                name: "IX_MediaFiles_StackGroupId",
                table: "MediaFiles",
                column: "StackGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_EpisodeFileLinks_MediaFileId",
                table: "EpisodeFileLinks",
                column: "MediaFileId");

            migrationBuilder.CreateIndex(
                name: "IX_EpisodeFileLinks_TvEpisodeId_MediaFileId_OrderInFile",
                table: "EpisodeFileLinks",
                columns: new[] { "TvEpisodeId", "MediaFileId", "OrderInFile" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExclusionRules_RuleId",
                table: "ExclusionRules",
                column: "RuleId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibraryRoots_Path",
                table: "LibraryRoots",
                column: "Path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NfoMetadata_SourcePath",
                table: "NfoMetadata",
                column: "SourcePath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReviewItems_FilePath",
                table: "ReviewItems",
                column: "FilePath");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewItems_FilePath_Status",
                table: "ReviewItems",
                columns: new[] { "FilePath", "Status" },
                unique: true,
                filter: "[Status] = 'Open'");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewItems_Status",
                table: "ReviewItems",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ScanItemDecisions_FilePath",
                table: "ScanItemDecisions",
                column: "FilePath");

            migrationBuilder.CreateIndex(
                name: "IX_ScanItemDecisions_MediaFileId",
                table: "ScanItemDecisions",
                column: "MediaFileId");

            migrationBuilder.CreateIndex(
                name: "IX_ScanItemDecisions_ReviewItemId",
                table: "ScanItemDecisions",
                column: "ReviewItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ScanItemDecisions_ScanRunId_FilePath",
                table: "ScanItemDecisions",
                columns: new[] { "ScanRunId", "FilePath" });

            migrationBuilder.CreateIndex(
                name: "IX_ScanRuns_StartedAt",
                table: "ScanRuns",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ScanRuns_Status",
                table: "ScanRuns",
                column: "Status",
                unique: true,
                filter: "[Status] = 'Running'");

            migrationBuilder.CreateIndex(
                name: "IX_StackGroups_MediaId",
                table: "StackGroups",
                column: "MediaId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StackGroups_MediaId1",
                table: "StackGroups",
                column: "MediaId1",
                unique: true,
                filter: "[MediaId1] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_MediaFiles_LibraryRoots_LibraryRootId",
                table: "MediaFiles",
                column: "LibraryRootId",
                principalTable: "LibraryRoots",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_MediaFiles_StackGroups_StackGroupId",
                table: "MediaFiles",
                column: "StackGroupId",
                principalTable: "StackGroups",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Medias_NfoMetadata_NfoMetadataId",
                table: "Medias",
                column: "NfoMetadataId",
                principalTable: "NfoMetadata",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MediaFiles_LibraryRoots_LibraryRootId",
                table: "MediaFiles");

            migrationBuilder.DropForeignKey(
                name: "FK_MediaFiles_StackGroups_StackGroupId",
                table: "MediaFiles");

            migrationBuilder.DropForeignKey(
                name: "FK_Medias_NfoMetadata_NfoMetadataId",
                table: "Medias");

            migrationBuilder.DropTable(
                name: "EpisodeFileLinks");

            migrationBuilder.DropTable(
                name: "ExclusionRules");

            migrationBuilder.DropTable(
                name: "LibraryRoots");

            migrationBuilder.DropTable(
                name: "NfoMetadata");

            migrationBuilder.DropTable(
                name: "ScanItemDecisions");

            migrationBuilder.DropTable(
                name: "StackGroups");

            migrationBuilder.DropTable(
                name: "ReviewItems");

            migrationBuilder.DropTable(
                name: "ScanRuns");

            migrationBuilder.DropIndex(
                name: "IX_Medias_NfoMetadataId",
                table: "Medias");

            migrationBuilder.DropIndex(
                name: "IX_MediaFiles_LibraryRootId",
                table: "MediaFiles");

            migrationBuilder.DropIndex(
                name: "IX_MediaFiles_LibraryRootId_Fingerprint",
                table: "MediaFiles");

            migrationBuilder.DropIndex(
                name: "IX_MediaFiles_MissingSince",
                table: "MediaFiles");

            migrationBuilder.DropIndex(
                name: "IX_MediaFiles_StackGroupId",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "NfoMetadataId",
                table: "Medias");

            migrationBuilder.DropColumn(
                name: "Year",
                table: "Medias");

            migrationBuilder.DropColumn(
                name: "Fingerprint",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "FirstSeenScanRunId",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "LastSeenScanRunId",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "LibraryRootId",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "MissingSince",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "MtimeUtc",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "StackGroupId",
                table: "MediaFiles");
        }
    }
}
