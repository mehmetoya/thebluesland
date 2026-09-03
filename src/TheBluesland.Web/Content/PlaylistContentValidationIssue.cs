namespace TheBluesland.Web.Content;

/// <summary>
/// One field-level validation failure found by <see cref="PlaylistContentValidator"/> (US-006).
/// Carries enough structure - file name, field name and a human-readable reason - that a future
/// CI reporter (US-007) can print "which file violated which rule" (spec section 18.1) without
/// re-parsing a plain exception message.
/// </summary>
public sealed record PlaylistContentValidationIssue(string FileName, string Field, string Message);
