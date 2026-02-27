using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaHandler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WishlistItemUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WishlistItems_UserId_TmdbId",
                table: "WishlistItems");

            migrationBuilder.CreateIndex(
                name: "IX_WishlistItems_UserId_TmdbId",
                table: "WishlistItems",
                columns: new[] { "UserId", "TmdbId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WishlistItems_UserId_TmdbId",
                table: "WishlistItems");

            migrationBuilder.CreateIndex(
                name: "IX_WishlistItems_UserId_TmdbId",
                table: "WishlistItems",
                columns: new[] { "UserId", "TmdbId" });
        }
    }
}
