namespace TheBluesland.WeeklyNewsletter.Drafting;

/// <summary>The two issue languages and the fixed strings the code (not the model) writes.</summary>
public sealed record DraftLanguage(string Code, string Name, string TitleFormat, string WhyLabel, string SourcesLabel, string BackupLabel, string AiNote)
{
    public static readonly DraftLanguage Turkish = new(
        "tr",
        "Turkish",
        "TheBluesland Weekly · {0}. hafta",
        "Neden önemli:",
        "Kaynaklar:",
        "Yedek",
        "Bu sayı yapay zekâ yardımıyla hazırlandı ve TheBluesland editörü tarafından düzenlendi.");

    public static readonly DraftLanguage English = new(
        "en",
        "English",
        "TheBluesland Weekly · Week {0}",
        "Why it matters:",
        "Sources:",
        "Backup",
        "This issue was prepared with AI assistance and edited by TheBluesland's editor.");

    public static IReadOnlyList<DraftLanguage> All { get; } = [Turkish, English];
}
