using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBluesland.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEraBucketCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int[]>(
                name: "era_bucket_counts",
                table: "spotify_playlist_cache",
                type: "integer[]",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "era_bucket_counts",
                table: "spotify_playlist_cache");
        }
    }
}
