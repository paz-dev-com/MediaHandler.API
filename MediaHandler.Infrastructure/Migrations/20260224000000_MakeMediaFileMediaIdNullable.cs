using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaHandler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MakeMediaFileMediaIdNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MediaFiles_Medias_MediaId",
                table: "MediaFiles");

            migrationBuilder.AlterColumn<Guid>(
                name: "MediaId",
                table: "MediaFiles",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddForeignKey(
                name: "FK_MediaFiles_Medias_MediaId",
                table: "MediaFiles",
                column: "MediaId",
                principalTable: "Medias",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MediaFiles_Medias_MediaId",
                table: "MediaFiles");

            migrationBuilder.AlterColumn<Guid>(
                name: "MediaId",
                table: "MediaFiles",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_MediaFiles_Medias_MediaId",
                table: "MediaFiles",
                column: "MediaId",
                principalTable: "Medias",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
