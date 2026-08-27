using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace task_list.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAvatarColorIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AvatarColorIndex",
                table: "AspNetUsers",
                type: "int",
                nullable: false,
                // -1 = renk indeksi henuz atanmadi; uygulama acilisinda
                // IUserAvatarColorService.BackfillAsync tekil indeksleri dagitir.
                defaultValue: -1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AvatarColorIndex",
                table: "AspNetUsers");
        }
    }
}
