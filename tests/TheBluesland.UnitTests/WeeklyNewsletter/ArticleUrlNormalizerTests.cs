using Shouldly;
using TheBluesland.WeeklyNewsletter.Ingest;
using Xunit;

namespace TheBluesland.UnitTests.WeeklyNewsletter;

/// <summary>
/// news-ingest spec, Design section 2: normalized_url is the de-duplication key, so every variant
/// a feed (or a share link) may emit for the same article must collapse to one string.
/// </summary>
public sealed class ArticleUrlNormalizerTests
{
    private const string Canonical = "https://nme.com/news/music/u2-tate-modern";

    [Theory]
    [InlineData("https://www.nme.com/news/music/u2-tate-modern")]
    [InlineData("http://www.nme.com/news/music/u2-tate-modern")]
    [InlineData("https://WWW.NME.COM/news/music/u2-tate-modern")]
    [InlineData("https://www.nme.com/news/music/u2-tate-modern/")]
    [InlineData("https://www.nme.com/news/music/u2-tate-modern#comments")]
    [InlineData("https://www.nme.com:443/news/music/u2-tate-modern")]
    [InlineData("https://www.nme.com/news/music/u2-tate-modern?utm_source=rss&utm_medium=rss&utm_campaign=u2")]
    [InlineData("https://www.nme.com/news/music/u2-tate-modern?fbclid=abc123")]
    [InlineData("https://www.nme.com/news/music/u2-tate-modern?gclid=x&mc_cid=y&mc_eid=z&ref=home&cmpid=nl")]
    [InlineData("https://www.nme.com/news/music/u2-tate-modern?UTM_Source=Twitter")]
    [InlineData("http://WWW.nme.com/news/music/u2-tate-modern/?utm_source=rss#top")]
    public void Normalize_collapses_tracking_and_presentation_variants_to_one_key(string url)
    {
        ArticleUrlNormalizer.Normalize(new Uri(url)).ShouldBe(Canonical);
    }

    [Fact]
    public void Normalize_keeps_path_case_because_paths_are_case_sensitive()
    {
        ArticleUrlNormalizer.Normalize(new Uri("https://example.com/News/Story-ABC"))
            .ShouldBe("https://example.com/News/Story-ABC");
    }

    [Fact]
    public void Normalize_keeps_meaningful_query_parameters_sorted_by_name()
    {
        ArticleUrlNormalizer.Normalize(new Uri("https://example.com/story?page=2&id=17&utm_source=rss"))
            .ShouldBe("https://example.com/story?id=17&page=2");
    }

    [Fact]
    public void Normalize_same_parameters_in_different_order_give_the_same_key()
    {
        var first = ArticleUrlNormalizer.Normalize(new Uri("https://example.com/story?b=2&a=1"));
        var second = ArticleUrlNormalizer.Normalize(new Uri("https://example.com/story?a=1&b=2"));

        first.ShouldBe(second);
    }

    [Fact]
    public void Normalize_keeps_the_root_path_slash()
    {
        ArticleUrlNormalizer.Normalize(new Uri("https://www.example.com/")).ShouldBe("https://example.com/");
    }

    [Fact]
    public void Normalize_keeps_a_non_default_port()
    {
        ArticleUrlNormalizer.Normalize(new Uri("https://example.com:8443/story/")).ShouldBe("https://example.com:8443/story");
    }

    [Fact]
    public void Normalize_does_not_strip_www_from_the_middle_of_a_host()
    {
        ArticleUrlNormalizer.Normalize(new Uri("https://news.www-music.com/a")).ShouldBe("https://news.www-music.com/a");
    }

    [Fact]
    public void Normalize_keeps_parameters_that_merely_start_with_ref_or_contain_utm()
    {
        ArticleUrlNormalizer.Normalize(new Uri("https://example.com/s?reference=1&notutm=2"))
            .ShouldBe("https://example.com/s?notutm=2&reference=1");
    }

    [Fact]
    public void Normalize_keeps_percent_encoding_of_remaining_values()
    {
        ArticleUrlNormalizer.Normalize(new Uri("https://example.com/search?q=rock%20%26%20roll"))
            .ShouldBe("https://example.com/search?q=rock%20%26%20roll");
    }

    [Fact]
    public void Normalize_is_idempotent()
    {
        var once = ArticleUrlNormalizer.Normalize(new Uri("http://www.Example.com/a/?utm_source=x&z=1#f"));

        ArticleUrlNormalizer.Normalize(new Uri(once)).ShouldBe(once);
    }
}
