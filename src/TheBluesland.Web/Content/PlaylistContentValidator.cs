using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TheBluesland.Web.Content;

/// <summary>
/// Validates every <c>content/playlists/*.md</c> file against the v0.2 editorial schema and
/// taxonomy (spec section 8-9): required fields, field-level format/range rules, approved
/// taxonomy values, and slug/spotifyPlaylistId uniqueness across files. Decoupled from
/// <see cref="PlaylistContentReader"/> (the render path) per that reader's own doc comment - this
/// is US-006's job, not the reader's. Collects every violation across every file rather than
/// throwing on the first one, so a future CI step (US-007) can report them all in one run (spec
/// section 18.1).
/// </summary>
public sealed class PlaylistContentValidator
{
    private const string DraftStatus = "draft";
    private const string PublishedStatus = "published";
    private const int SupportedSchemaVersion = 1;
    private const int SpotifyPlaylistIdLength = 22;
    private const int TitleMinLength = 3;
    private const int TitleMaxLength = 80;
    private const int SummaryMinLength = 40;
    private const int SummaryMaxLength = 180;
    private const int MoodsMaxCount = 5;
    private const int GenresMaxCount = 5;

    private static readonly Regex SlugPattern = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);
    private static readonly Regex SpotifyPlaylistIdPattern = new("^[A-Za-z0-9]{22}$", RegexOptions.Compiled);

    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// Validates every <c>*.md</c> file directly under <paramref name="contentDirectory"/>.
    /// Returns an empty (valid) result, rather than throwing, if the directory does not exist -
    /// content/playlists may legitimately not exist yet.
    /// </summary>
    public async Task<PlaylistContentValidationResult> ValidateAllAsync(
        string contentDirectory,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(contentDirectory))
        {
            return new PlaylistContentValidationResult([]);
        }

        var issues = new List<PlaylistContentValidationIssue>();
        var slugOwners = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var spotifyPlaylistIdOwners = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var filePath in Directory
                     .EnumerateFiles(contentDirectory, "*.md", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileName = Path.GetFileName(filePath);
            var rawContent = await File.ReadAllTextAsync(filePath, cancellationToken);
            var (yaml, body) = FrontMatterSplitter.Split(rawContent);
            if (yaml is null)
            {
                issues.Add(new PlaylistContentValidationIssue(
                    fileName,
                    "frontMatter",
                    "No YAML front-matter block found (missing opening/closing '---')."));
                continue;
            }

            PlaylistValidationFrontMatter frontMatter;
            try
            {
                frontMatter = _deserializer.Deserialize<PlaylistValidationFrontMatter?>(yaml)
                    ?? new PlaylistValidationFrontMatter();
            }
            catch (YamlException ex)
            {
                issues.Add(new PlaylistContentValidationIssue(fileName, "frontMatter", $"Front matter is not valid YAML: {ex.Message}"));
                continue;
            }

            ValidateFields(fileName, frontMatter, body, issues);

            if (frontMatter.Slug is { Length: > 0 } slug)
            {
                CollectOwner(slugOwners, slug, fileName);
            }

            if (frontMatter.SpotifyPlaylistId is { Length: > 0 } spotifyPlaylistId)
            {
                CollectOwner(spotifyPlaylistIdOwners, spotifyPlaylistId, fileName);
            }
        }

        AddDuplicateIssues(issues, slugOwners, "slug");
        AddDuplicateIssues(issues, spotifyPlaylistIdOwners, "spotifyPlaylistId");

        return new PlaylistContentValidationResult(issues);
    }

    private static void CollectOwner(Dictionary<string, List<string>> owners, string value, string fileName)
    {
        if (!owners.TryGetValue(value, out var files))
        {
            files = [];
            owners[value] = files;
        }

        files.Add(fileName);
    }

    private static void AddDuplicateIssues(
        List<PlaylistContentValidationIssue> issues,
        Dictionary<string, List<string>> owners,
        string field)
    {
        foreach (var (value, files) in owners)
        {
            if (files.Count < 2)
            {
                continue;
            }

            foreach (var fileName in files)
            {
                var otherFiles = string.Join(", ", files.Where(f => f != fileName));
                issues.Add(new PlaylistContentValidationIssue(
                    fileName,
                    field,
                    $"{field} '{value}' is not unique; also used by: {otherFiles}."));
            }
        }
    }

    private static void ValidateFields(
        string fileName,
        PlaylistValidationFrontMatter frontMatter,
        string body,
        List<PlaylistContentValidationIssue> issues)
    {
        void AddIssue(string field, string message) =>
            issues.Add(new PlaylistContentValidationIssue(fileName, field, message));

        var isPublished = string.Equals(frontMatter.Status, PublishedStatus, StringComparison.Ordinal);

        if (frontMatter.SchemaVersion is not { } schemaVersion)
        {
            AddIssue("schemaVersion", "schemaVersion is required.");
        }
        else if (schemaVersion != SupportedSchemaVersion)
        {
            AddIssue("schemaVersion", $"schemaVersion must equal the currently supported version ({SupportedSchemaVersion}).");
        }

        if (frontMatter.Slug is not { Length: > 0 } slug)
        {
            AddIssue("slug", "slug is required.");
        }
        else if (!SlugPattern.IsMatch(slug))
        {
            AddIssue("slug", "slug must be lowercase kebab-case (e.g. 'my-playlist-name').");
        }

        if (frontMatter.SpotifyPlaylistId is not { Length: > 0 } spotifyPlaylistId)
        {
            AddIssue("spotifyPlaylistId", "spotifyPlaylistId is required.");
        }
        else if (!SpotifyPlaylistIdPattern.IsMatch(spotifyPlaylistId))
        {
            AddIssue(
                "spotifyPlaylistId",
                $"spotifyPlaylistId must be exactly {SpotifyPlaylistIdLength} base62 characters (A-Za-z0-9), found {spotifyPlaylistId.Length}.");
        }

        if (frontMatter.Status is not { Length: > 0 })
        {
            AddIssue("status", "status is required.");
        }
        else if (frontMatter.Status is not (DraftStatus or PublishedStatus))
        {
            AddIssue("status", $"status must be '{DraftStatus}' or '{PublishedStatus}'.");
        }

        // title is required regardless of draft/published status: spec section 9.1 gives
        // publishedAt and the Markdown body an explicit "required for published content"
        // qualifier but does not give title one, and section 9.3's draft exemption names only
        // publishedAt ("draft content may omit publication date but must still have a valid
        // playlist ID and slug") - so this exemption is deliberately not widened to title.
        if (frontMatter.Title is not { Length: > 0 } title)
        {
            AddIssue("title", "title is required.");
        }
        else if (title.Length is < TitleMinLength or > TitleMaxLength)
        {
            AddIssue("title", $"title must be {TitleMinLength}-{TitleMaxLength} characters, found {title.Length}.");
        }

        // summary, moods, genres, occasions and era are required only for published content
        // (spec 9.3's draft exemption applied consistently across the fields the story names as
        // published-only); when present on a draft, values are still validated so a bogus value
        // cannot ride along until publication.
        if (frontMatter.Summary is not { Length: > 0 } summary)
        {
            if (isPublished)
            {
                AddIssue("summary", "summary is required for published content.");
            }
        }
        else if (summary.Length is < SummaryMinLength or > SummaryMaxLength)
        {
            AddIssue("summary", $"summary must be {SummaryMinLength}-{SummaryMaxLength} characters, found {summary.Length}.");
        }

        ValidateTaxonomyArray(AddIssue, "moods", frontMatter.Moods, PlaylistTaxonomy.Moods, MoodsMaxCount, isPublished);
        ValidateTaxonomyArray(AddIssue, "genres", frontMatter.Genres, PlaylistTaxonomy.Genres, GenresMaxCount, isPublished);
        ValidateTaxonomyArray(AddIssue, "occasions", frontMatter.Occasions, PlaylistTaxonomy.Occasions, maxCount: null, isPublished);

        if (frontMatter.Era is not { Length: > 0 } era)
        {
            if (isPublished)
            {
                AddIssue("era", "era is required for published content.");
            }
        }
        else if (!PlaylistTaxonomy.Eras.Contains(era, StringComparer.Ordinal))
        {
            AddIssue("era", $"era '{era}' is not an approved value ({string.Join(", ", PlaylistTaxonomy.Eras)}).");
        }

        // publishedAt: the one field the acceptance criteria explicitly names as draft-exempt.
        if (frontMatter.PublishedAt is not { Length: > 0 } && isPublished)
        {
            AddIssue("publishedAt", "publishedAt is required for published content.");
        }

        // Markdown body (curator note): spec 9.1 lists it as "required for published content".
        if (isPublished && string.IsNullOrWhiteSpace(body))
        {
            AddIssue("body", "Markdown body (curator note) is required for published content.");
        }
    }

    private static void ValidateTaxonomyArray(
        Action<string, string> addIssue,
        string field,
        string[]? values,
        IReadOnlyCollection<string> approvedValues,
        int? maxCount,
        bool isPublished)
    {
        if (values is not { Length: > 0 })
        {
            if (isPublished)
            {
                addIssue(field, $"{field} is required for published content.");
            }

            return;
        }

        if (maxCount is { } max && values.Length > max)
        {
            addIssue(field, $"{field} must have at most {max} values, found {values.Length}.");
        }

        foreach (var value in values)
        {
            if (!approvedValues.Contains(value, StringComparer.Ordinal))
            {
                addIssue(field, $"{field} value '{value}' is not an approved value ({string.Join(", ", approvedValues)}).");
            }
        }
    }

}
