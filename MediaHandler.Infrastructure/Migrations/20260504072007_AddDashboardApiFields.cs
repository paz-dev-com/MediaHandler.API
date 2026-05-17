using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaHandler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDashboardApiFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Kind",
                table: "ScanItemDecisions",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<int>(
                name: "AssignedTmdbId",
                table: "ScanItemDecisions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssignedTmdbKind",
                table: "ScanItemDecisions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CandidatesJson",
                table: "ScanItemDecisions",
                type: "nvarchar(max)",
                nullable: true,
                defaultValue: "[]");

            migrationBuilder.AddColumn<Guid>(
                name: "LibraryRootId",
                table: "ScanItemDecisions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ParsedEpisode",
                table: "ScanItemDecisions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParsedMediaType",
                table: "ScanItemDecisions",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ParsedSeason",
                table: "ScanItemDecisions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParsedTitle",
                table: "ScanItemDecisions",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ParsedYear",
                table: "ScanItemDecisions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NumberOfEpisodes",
                table: "Medias",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NumberOfSeasons",
                table: "Medias",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Medias",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EnrichmentRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    FailureReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TotalItems = table.Column<int>(type: "int", nullable: false),
                    EnrichedCount = table.Column<int>(type: "int", nullable: false),
                    FailedCount = table.Column<int>(type: "int", nullable: false),
                    SkippedCount = table.Column<int>(type: "int", nullable: false),
                    CurrentItem = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ErrorDetailsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnrichmentRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScanItemDecisions_LibraryRootId",
                table: "ScanItemDecisions",
                column: "LibraryRootId");

            migrationBuilder.CreateIndex(
                name: "IX_ScanItemDecisions_ScanRunId_Kind",
                table: "ScanItemDecisions",
                columns: new[] { "ScanRunId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_ScanItemDecisions_ScanRunId_ParsedMediaType",
                table: "ScanItemDecisions",
                columns: new[] { "ScanRunId", "ParsedMediaType" });

            migrationBuilder.CreateIndex(
                name: "IX_ScanItemDecisions_ScanRunId_ParsedTitle",
                table: "ScanItemDecisions",
                columns: new[] { "ScanRunId", "ParsedTitle" });

            migrationBuilder.CreateIndex(
                name: "UX_EnrichmentRuns_Running",
                table: "EnrichmentRuns",
                column: "Status",
                unique: true,
                filter: "[Status] = 'Running'");

            migrationBuilder.AddForeignKey(
                name: "FK_ScanItemDecisions_LibraryRoots_LibraryRootId",
                table: "ScanItemDecisions",
                column: "LibraryRootId",
                principalTable: "LibraryRoots",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ScanItemDecisions_LibraryRoots_LibraryRootId",
                table: "ScanItemDecisions");

            migrationBuilder.DropTable(
                name: "EnrichmentRuns");

            migrationBuilder.DropIndex(
                name: "IX_ScanItemDecisions_LibraryRootId",
                table: "ScanItemDecisions");

            migrationBuilder.DropIndex(
                name: "IX_ScanItemDecisions_ScanRunId_Kind",
                table: "ScanItemDecisions");

            migrationBuilder.DropIndex(
                name: "IX_ScanItemDecisions_ScanRunId_ParsedMediaType",
                table: "ScanItemDecisions");

            migrationBuilder.DropIndex(
                name: "IX_ScanItemDecisions_ScanRunId_ParsedTitle",
                table: "ScanItemDecisions");

            migrationBuilder.DropColumn(
                name: "AssignedTmdbId",
                table: "ScanItemDecisions");

            migrationBuilder.DropColumn(
                name: "AssignedTmdbKind",
                table: "ScanItemDecisions");

            migrationBuilder.DropColumn(
                name: "CandidatesJson",
                table: "ScanItemDecisions");

            migrationBuilder.DropColumn(
                name: "LibraryRootId",
                table: "ScanItemDecisions");

            migrationBuilder.DropColumn(
                name: "ParsedEpisode",
                table: "ScanItemDecisions");

            migrationBuilder.DropColumn(
                name: "ParsedMediaType",
                table: "ScanItemDecisions");

            migrationBuilder.DropColumn(
                name: "ParsedSeason",
                table: "ScanItemDecisions");

            migrationBuilder.DropColumn(
                name: "ParsedTitle",
                table: "ScanItemDecisions");

            migrationBuilder.DropColumn(
                name: "ParsedYear",
                table: "ScanItemDecisions");

            migrationBuilder.DropColumn(
                name: "NumberOfEpisodes",
                table: "Medias");

            migrationBuilder.DropColumn(
                name: "NumberOfSeasons",
                table: "Medias");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Medias");

            migrationBuilder.AlterColumn<string>(
                name: "Kind",
                table: "ScanItemDecisions",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");
        }
    }
}
