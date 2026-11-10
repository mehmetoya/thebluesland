using Shouldly;
using TheBluesland.WeeklyNewsletter.Drafting;
using TheBluesland.WeeklyNewsletter.Review;
using Xunit;
using static TheBluesland.UnitTests.WeeklyNewsletter.Drafting.DraftingTestData;
using static TheBluesland.UnitTests.WeeklyNewsletter.Review.ReviewTestData;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Review;

/// <summary>issue-review spec, Design section 4: what may reach main under content/weekly.</summary>
public sealed class WeeklyIssueValidatorTests
{
    private static readonly string Turkish = Draft(DraftLanguage.Turkish).IssueMarkdown;

    [Theory]
    [InlineData("tr")]
    [InlineData("en")]
    public void A_drafted_issue_is_valid(string code)
    {
        var language = DraftLanguage.All.Single(candidate => candidate.Code == code);

        WeeklyIssueValidator.Validate(IssueName(language), Draft(language).IssueMarkdown).ShouldBeEmpty();
    }

    [Fact]
    public void The_approved_sample_issue_drafted_into_a_file_is_valid()
    {
        var sample = SampleIssue("Türkçe", Card);

        WeeklyIssueValidator.Validate(IssueName(DraftLanguage.Turkish), IssueMarkdownWriter.Issue(Week46, DraftLanguage.Turkish, sample, Card)).ShouldBeEmpty();
    }

    [Fact]
    public void Windows_line_endings_and_extra_front_matter_fields_are_accepted()
    {
        var edited = Turkish.Replace("aiAssisted: true\n", "aiAssisted: true\npreheader: \"later spec\"\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);

        WeeklyIssueValidator.Validate(IssueName(DraftLanguage.Turkish), edited).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("review/2026-W46.backups.tr.md")]
    [InlineData("2026-W46.backups.tr.md")]
    [InlineData("review/2026-W46.pr-body.md")]
    [InlineData("notes.md")]
    public void Backups_review_and_other_files_are_never_merged(string name)
    {
        WeeklyIssueValidator.Validate(name, Turkish).ShouldHaveSingleItem().ShouldContain("not an issue file");
    }

    [Fact]
    public void The_file_name_must_agree_with_the_front_matter()
    {
        var problems = WeeklyIssueValidator.Validate("2026-W47.tr.md", Turkish);

        problems.ShouldContain(problem => problem.Contains("issue must be 2026-W47", StringComparison.Ordinal));
        problems.ShouldContain(problem => problem.Contains("sendDate must be 2026-11-16", StringComparison.Ordinal));
    }

    [Fact]
    public void A_turkish_issue_saved_as_english_is_caught()
    {
        var problems = WeeklyIssueValidator.Validate(IssueName(DraftLanguage.English), Turkish);

        problems.ShouldContain(problem => problem.Contains("language must be en", StringComparison.Ordinal));
        problems.ShouldContain(problem => problem.Contains("AI note", StringComparison.Ordinal));
    }

    [Fact]
    public void A_heading_edited_without_its_front_matter_headline_is_caught()
    {
        var headline = Answer().For(Card.Picks.First().Key).Headline;

        var problems = WeeklyIssueValidator.Validate(IssueName(DraftLanguage.Turkish), Turkish.Replace($"## {headline}\n", "## Yeni başlık\n", StringComparison.Ordinal));

        problems.ShouldHaveSingleItem().ShouldContain("heading 1 differs");
    }

    [Fact]
    public void A_story_section_removed_from_the_body_is_caught()
    {
        var headline = Answer().For(Card.Picks.Last().Key).Headline;

        var problems = WeeklyIssueValidator.Validate(IssueName(DraftLanguage.Turkish), Turkish.Replace($"## {headline}\n", string.Empty, StringComparison.Ordinal));

        problems.ShouldHaveSingleItem().ShouldContain("4 story headings");
    }

    [Fact]
    public void Every_story_needs_an_https_source_and_a_visible_sources_line()
    {
        var link = Card.Picks.First().Links[0];
        var noHttps = Turkish.Replace($"url: \"{link.Url}\"", "url: \"http://example.test/a\"", StringComparison.Ordinal);
        var lines = Turkish.Split('\n').ToList();
        lines.RemoveAt(lines.FindIndex(line => line.StartsWith("Kaynaklar:", StringComparison.Ordinal)));
        var noLine = string.Join('\n', lines);

        WeeklyIssueValidator.Validate(IssueName(DraftLanguage.Turkish), noHttps).ShouldContain(problem => problem.Contains("story 1 has a source without a name or an https URL", StringComparison.Ordinal));
        WeeklyIssueValidator.Validate(IssueName(DraftLanguage.Turkish), noLine).ShouldHaveSingleItem().ShouldContain("story 1 has no \"Kaynaklar:\" line");
    }

    [Fact]
    public void The_ai_note_must_close_the_issue_word_for_word()
    {
        var problems = WeeklyIssueValidator.Validate(IssueName(DraftLanguage.Turkish), Turkish.Replace("editörü tarafından düzenlendi.", "editörü düzenledi.", StringComparison.Ordinal));

        problems.ShouldHaveSingleItem().ShouldContain("AI note");
    }

    [Fact]
    public void Missing_or_broken_front_matter_is_reported_without_quoting_the_text()
    {
        WeeklyIssueValidator.Validate(IssueName(DraftLanguage.Turkish), "Just prose.").ShouldHaveSingleItem().ShouldContain("missing front matter");
        WeeklyIssueValidator.Validate(IssueName(DraftLanguage.Turkish), "---\nissue: [unclosed\n---\n\nbody").ShouldHaveSingleItem().ShouldContain("not valid YAML");
        WeeklyIssueValidator.Validate("2026-W54.tr.md", Turkish).ShouldHaveSingleItem().ShouldContain("not an ISO week");
    }

    [Fact]
    public void A_directory_is_valid_when_missing_and_lists_each_problem_with_its_file()
    {
        WeeklyIssueValidator.ValidateDirectory(Path.Combine(Path.GetTempPath(), $"none-{Guid.NewGuid():N}")).ShouldBeEmpty();

        var directory = Path.Combine(Path.GetTempPath(), $"weekly-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "review"));
            File.WriteAllText(Path.Combine(directory, IssueName(DraftLanguage.Turkish)), Turkish);
            File.WriteAllText(Path.Combine(directory, "review", "2026-W46.backups.tr.md"), "x");

            WeeklyIssueValidator.ValidateDirectory(directory).ShouldHaveSingleItem().ShouldStartWith("review/2026-W46.backups.tr.md: not an issue file");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
