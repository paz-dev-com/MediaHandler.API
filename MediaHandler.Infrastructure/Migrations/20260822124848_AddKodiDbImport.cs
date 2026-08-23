using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaHandler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKodiDbImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "ReviewItems",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "Scan");

            migrationBuilder.CreateTable(
                name: "ImportRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SourceFileName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false),
                    UploadedFilePath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PathMappingsJson = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "[]"),
                    UnmatchedPrefixesJson = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "[]"),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    TotalItems = table.Column<int>(type: "int", nullable: false),
                    MoviesCreated = table.Column<int>(type: "int", nullable: false),
                    ShowsCreated = table.Column<int>(type: "int", nullable: false),
                    EpisodesCreated = table.Column<int>(type: "int", nullable: false),
                    ItemsReused = table.Column<int>(type: "int", nullable: false),
                    ItemsUnchanged = table.Column<int>(type: "int", nullable: false),
                    FilesLinked = table.Column<int>(type: "int", nullable: false),
                    UnmatchedPaths = table.Column<int>(type: "int", nullable: false),
                    NoScannedFiles = table.Column<int>(type: "int", nullable: false),
                    UnsupportedLocations = table.Column<int>(type: "int", nullable: false),
                    Conflicts = table.Column<int>(type: "int", nullable: false),
                    NoLongerInKodi = table.Column<int>(type: "int", nullable: false),
                    NeedsReview = table.Column<int>(type: "int", nullable: false),
                    IdentityLookupFailures = table.Column<int>(type: "int", nullable: false),
                    SkippedMusicVideos = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KodiPathMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KodiPrefix = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    NasPrefix = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KodiPathMappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImportItemOutcomes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImportRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    KodiItemKind = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    KodiItemId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    MediaKind = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    LinkOutcome = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LinkedFileCount = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    KodiPathPrefix = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    MediaId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MediaFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportItemOutcomes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportItemOutcomes_ImportRuns_ImportRunId",
                        column: x => x.ImportRunId,
                        principalTable: "ImportRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImportItemOutcomes_ImportRunId",
                table: "ImportItemOutcomes",
                column: "ImportRunId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportItemOutcomes_ImportRunId_Outcome",
                table: "ImportItemOutcomes",
                columns: new[] { "ImportRunId", "Outcome" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportItemOutcomes_KodiItemKind_KodiItemId",
                table: "ImportItemOutcomes",
                columns: new[] { "KodiItemKind", "KodiItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_ImportRuns_StartedAt",
                table: "ImportRuns",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ImportRuns_Status",
                table: "ImportRuns",
                column: "Status",
                unique: true,
                filter: "[Status] = 'Running'");

            migrationBuilder.CreateIndex(
                name: "IX_KodiPathMappings_KodiPrefix",
                table: "KodiPathMappings",
                column: "KodiPrefix",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KodiPathMappings_SortOrder",
                table: "KodiPathMappings",
                column: "SortOrder");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImportItemOutcomes");

            migrationBuilder.DropTable(
                name: "KodiPathMappings");

            migrationBuilder.DropTable(
                name: "ImportRuns");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "ReviewItems");
        }
    }
}
