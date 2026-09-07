using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBluesland.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddComputedPlaylistEras : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "computed_eras",
                table: "spotify_playlist_cache",
                type: "text[]",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "computed_eras",
                table: "spotify_playlist_cache");
        }
    }
}
