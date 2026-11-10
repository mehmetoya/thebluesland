using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TheBluesland.Data.Migrations.NewsletterDb
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "newsletter");

            migrationBuilder.CreateTable(
                name: "news_article",
                schema: "newsletter",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    source = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    url = table.Column<string>(type: "text", nullable: false),
                    normalized_url = table.Column<string>(type: "text", nullable: false),
                    summary = table.Column<string>(type: "text", nullable: true),
                    categories = table.Column<List<string>>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]"),
                    published_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_news_article", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_news_article_normalized_url",
                schema: "newsletter",
                table: "news_article",
                column: "normalized_url",
                unique: true);

            // news-ingest spec, Design section 1: story-clustering reads "the last 7 days" by the
            // article's effective time. Expression index - EF Core cannot model it, so raw SQL.
            migrationBuilder.Sql(
                "CREATE INDEX ix_news_article_effective_time ON newsletter.news_article ((coalesce(published_at, fetched_at)));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS newsletter.ix_news_article_effective_time;");

            migrationBuilder.DropTable(
                name: "news_article",
                schema: "newsletter");
        }
    }
}
