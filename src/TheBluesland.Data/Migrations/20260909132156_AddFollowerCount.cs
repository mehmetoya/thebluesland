using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBluesland.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFollowerCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "follower_count",
                table: "spotify_playlist_cache",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "follower_count",
                table: "spotify_playlist_cache");
        }
    }
}
