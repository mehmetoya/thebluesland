using TheBluesland.WeeklyNewsletter.Drafting;
using static TheBluesland.UnitTests.WeeklyNewsletter.Drafting.DraftingTestData;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Review;

internal static class ReviewTestData
{
    public static readonly FactCard Card = W46Card();

    /// <summary>A full model answer (picks and backups) for one language, as DraftCommand would get it.</summary>
    public static DraftResponse Answer() => DraftResponse.Parse(FakeAnswer(Card.Stories.Select(story => story.Key)), Card);

    public static LanguageDraft Draft(DraftLanguage language, DraftResponse? text = null, IReadOnlyList<DraftWarning>? warnings = null)
    {
        var answer = text ?? Answer();
        return new LanguageDraft(
            language,
            IssueMarkdownWriter.Issue(Week46, language, answer, Card),
            IssueMarkdownWriter.Backups(Week46, language, answer, Card),
            answer,
            warnings ?? [],
            "test-model");
    }

    public static string IssueName(DraftLanguage language) => IssueMarkdownWriter.IssueFileName(Week46, language);
}
