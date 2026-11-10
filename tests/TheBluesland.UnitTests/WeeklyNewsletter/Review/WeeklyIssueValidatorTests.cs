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

    // DD-14: optional front matter `playlist: <slug>` links the issue to a playlist.
    [Fact]
    public void The_optional_playlist_slug_is_read_and_must_be_kebab_case()
    {
        var withPlaylist = Turkish.Replace("aiAssisted: true\n", "aiAssisted: true\nplaylist: anadolu-nights\n", StringComparison.Ordinal);

        WeeklyIssueValidator.Validate(IssueName(DraftLanguage.Turkish), withPlaylist).ShouldBeEmpty();
        WeeklyIssueValidator.Read(IssueName(DraftLanguage.Turkish), withPlaylist)!.Playlist.ShouldBe("anadolu-nights");
        WeeklyIssueValidator.Read(IssueName(DraftLanguage.Turkish), Turkish)!.Playlist.ShouldBeNull();

        var bad = Turkish.Replace("aiAssisted: true\n", "aiAssisted: true\nplaylist: \"../Not A Slug\"\n", StringComparison.Ordinal);
        WeeklyIssueValidator.Validate(IssueName(DraftLanguage.Turkish), bad).ShouldHaveSingleItem().ShouldContain("playlist must be a kebab-case");
    }

    [Fact]
    public void The_playlist_slug_must_name_a_file_in_content_playlists_when_that_folder_is_visible()
    {
        var root = Path.Combine(Path.GetTempPath(), "weekly-playlist-" + Guid.NewGuid().ToString("N"));
        var weekly = Path.Combine(root, "weekly");
        var playlists = Path.Combine(root, "playlists");
        Directory.CreateDirectory(weekly);
        Directory.CreateDirectory(playlists);
        try
        {
            File.WriteAllText(Path.Combine(playlists, "anadolu-nights.md"), "---\n---\n");
            File.WriteAllText(
                Path.Combine(weekly, IssueName(DraftLanguage.Turkish)),
                Turkish.Replace("aiAssisted: true\n", "aiAssisted: true\nplaylist: anadolu-nights\n", StringComparison.Ordinal));
            WeeklyIssueValidator.ValidateDirectory(weekly).ShouldBeEmpty();

            File.WriteAllText(
                Path.Combine(weekly, IssueName(DraftLanguage.Turkish)),
                Turkish.Replace("aiAssisted: true\n", "aiAssisted: true\nplaylist: no-such-playlist\n", StringComparison.Ordinal));
            WeeklyIssueValidator.ValidateDirectory(weekly).ShouldHaveSingleItem().ShouldContain("not a file in content/playlists");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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
    public void The_writer_ends_every_issue_with_the_credit_line()
    {
        // ADR-0006 madde 4, 2026-11-10: the credit line is the transparency disclosure.
        DraftLanguage.Turkish.AiNote.ShouldBe("Bu sayı TheBluesland ve Gemini'nin elinden.");
        DraftLanguage.English.AiNote.ShouldBe("This issue comes from TheBluesland and Gemini.");
        Turkish.TrimEnd().ShouldEndWith($"\n\n*{DraftLanguage.Turkish.AiNote}*");
        Draft(DraftLanguage.English).IssueMarkdown.TrimEnd().ShouldEndWith($"\n\n*{DraftLanguage.English.AiNote}*");
    }

    [Fact]
    public void An_earlier_issue_without_any_note_stays_valid()
    {
        var noNote = Turkish.Replace($"\n*{DraftLanguage.Turkish.AiNote}*\n", string.Empty, StringComparison.Ordinal);

        noNote.ShouldNotContain(DraftLanguage.Turkish.AiNote);
        WeeklyIssueValidator.Validate(IssueName(DraftLanguage.Turkish), noNote).ShouldBeEmpty();
    }

    [Fact]
    public void An_issue_sent_from_w47_on_must_carry_the_credit_line()
    {
        string W47(string markdown) => markdown.Replace("2026-W46", "2026-W47", StringComparison.Ordinal).Replace("sendDate: 2026-11-09", "sendDate: 2026-11-16", StringComparison.Ordinal);
        var name = "2026-W47.tr.md";
        var noNote = W47(Turkish.Replace($"\n*{DraftLanguage.Turkish.AiNote}*\n", string.Empty, StringComparison.Ordinal));
        var legacy = noNote.TrimEnd() + $"\n\n*{DraftLanguage.Turkish.LegacyAiNote}*\n";

        WeeklyIssueValidator.Validate(name, W47(Turkish)).ShouldBeEmpty();
        WeeklyIssueValidator.Validate(name, noNote).ShouldHaveSingleItem().ShouldContain("credit line");
        WeeklyIssueValidator.Validate(name, legacy).ShouldHaveSingleItem().ShouldContain("credit line");
    }

    [Fact]
    public void Issues_with_the_note_of_the_first_drafts_stay_valid()
    {
        var legacy = Turkish.TrimEnd() + $"\n\n*{DraftLanguage.Turkish.LegacyAiNote}*\n";

        WeeklyIssueValidator.Validate(IssueName(DraftLanguage.Turkish), legacy).ShouldBeEmpty();
    }

    [Fact]
    public void Subject_and_preview_are_optional_read_back_and_length_limited()
    {
        var name = IssueName(DraftLanguage.Turkish);
        var withBoth = Draft(DraftLanguage.Turkish, Answer() with { Subject = "Two hooks · TBL Weekly", Preview = "A line." }).IssueMarkdown;

        var withNeither = Draft(DraftLanguage.Turkish, Answer() with { Subject = null, Preview = null }).IssueMarkdown;
        WeeklyIssueValidator.Validate(name, withNeither).ShouldBeEmpty();
        var issue = WeeklyIssueValidator.Read(name, withBoth).ShouldNotBeNull();
        (issue.Subject, issue.Preview).ShouldBe(("Two hooks · TBL Weekly", "A line."));
        WeeklyIssueValidator.Read(name, withNeither).ShouldNotBeNull().Subject.ShouldBeNull();

        // The writer cuts over-long values itself; an editor's hand edit can still exceed the limits.
        var tooLong = withBoth
            .Replace("subject: \"Two hooks · TBL Weekly\"", $"subject: \"{new string('s', 61)}\"", StringComparison.Ordinal)
            .Replace("preview: \"A line.\"", $"preview: \"{new string('p', 91)}\"", StringComparison.Ordinal);
        WeeklyIssueValidator.Validate(name, tooLong).ShouldBe(
            ["subject must be 1-60 characters when present.", "preview must be 1-90 characters when present."]);
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
