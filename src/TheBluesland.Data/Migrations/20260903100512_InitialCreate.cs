using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBluesland.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "spotify_playlist_cache",
                columns: table => new
                {
                    spotify_playlist_id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    cover_image_url = table.Column<string>(type: "text", nullable: true),
                    track_count = table.Column<int>(type: "integer", nullable: false),
                    artists = table.Column<string[]>(type: "text[]", nullable: false),
                    spotify_snapshot_id = table.Column<string>(type: "text", nullable: true),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    is_available = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_spotify_playlist_cache", x => x.spotify_playlist_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "spotify_playlist_cache");
        }
    }
}
