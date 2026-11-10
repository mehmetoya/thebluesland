namespace TheBluesland.Web.Content;

/// <summary>The Kit forms the Weekly sign-up box posts to (Weekly:KitFormIdTr / Weekly:KitFormIdEn).</summary>
public static class WeeklySignup
{
    /// <summary>Language ("tr"/"en") and Kit subscription URL for every configured form, Turkish first.</summary>
    public static IReadOnlyList<(string Language, string Action)> Forms(IConfiguration configuration)
    {
        var forms = new List<(string Language, string Action)>();
        foreach (var (key, language) in new[] { ("Weekly:KitFormIdTr", "tr"), ("Weekly:KitFormIdEn", "en") })
        {
            if (long.TryParse(configuration[key], out var id) && id > 0)
            {
                forms.Add((language, $"https://app.kit.com/forms/{id}/subscriptions"));
            }
        }

        return forms;
    }
}
