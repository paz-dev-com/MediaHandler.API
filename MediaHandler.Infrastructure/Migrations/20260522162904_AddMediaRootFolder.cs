using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaHandler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaRootFolder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RootFolder",
                table: "Medias",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RootFolder",
                table: "Medias");
        }
    }
}
