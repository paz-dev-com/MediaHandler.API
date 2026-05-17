using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaHandler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEnrichedMediaIdsJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EnrichedMediaIdsJson",
                table: "EnrichmentRuns",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnrichedMediaIdsJson",
                table: "EnrichmentRuns");
        }
    }
}
