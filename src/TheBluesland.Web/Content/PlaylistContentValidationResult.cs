namespace TheBluesland.Web.Content;

/// <summary>
/// The outcome of validating every Markdown file under <c>content/playlists</c> (US-006). Never
/// throws on the first violation - <see cref="Issues"/> accumulates every violation across every
/// file so a caller (e.g. a future CI step, US-007) can report them all from one run instead of
/// fixing one violation at a time.
/// </summary>
public sealed record PlaylistContentValidationResult(IReadOnlyList<PlaylistContentValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}
