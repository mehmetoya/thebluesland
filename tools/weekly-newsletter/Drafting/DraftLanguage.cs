namespace TheBluesland.WeeklyNewsletter.Drafting;

/// <summary>The two issue languages and the fixed strings the code (not the model) writes.</summary>
public sealed record DraftLanguage(
    string Code,
    string Name,
    string TitleFormat,
    string WhyLabel,
    string SourcesLabel,
    string BackupLabel,
    string AiNote,
    string IntroTagline,
    string FallbackOutro)
{
    /// <summary>The brand tag every subject line ends with.</summary>
    public const string SubjectTag = " · TBL Weekly";

    public const int MaxSubject = 60;

    public const int MaxPreview = 90;

    /// <summary>
    /// The note the first drafts carried ("edited by"). Issues ending with it stay valid; new issues
    /// end with <see cref="AiNote"/>, the credit line (ADR-0006 madde 4).
    /// </summary>
    public string LegacyAiNote => Code == "tr"
        ? "Bu sayı yapay zekâ yardımıyla hazırlandı ve TheBluesland editörü tarafından düzenlendi."
        : "This issue was prepared with AI assistance and edited by TheBluesland's editor.";

    public static readonly DraftLanguage Turkish = new(
        "tr",
        "Turkish",
        "TheBluesland Weekly · {0}. hafta",
        "Neden önemli:",
        "Kaynaklar:",
        "Yedek",
        "Bu sayı TheBluesland ve Gemini'nin elinden.",
        "Beş haber, beş dakika.",
        "Haftaya pazartesi yine buradayız. İyi dinlemeler.");

    public static readonly DraftLanguage English = new(
        "en",
        "English",
        "TheBluesland Weekly · Week {0}",
        "Why it matters:",
        "Sources:",
        "Backup",
        "This issue comes from TheBluesland and Gemini.",
        "Five stories, five minutes.",
        "See you next Monday. Happy listening.");

    public static IReadOnlyList<DraftLanguage> All { get; } = [Turkish, English];
}
