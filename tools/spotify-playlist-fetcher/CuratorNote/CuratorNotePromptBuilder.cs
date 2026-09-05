using TheBluesland.Data.Entities;

namespace TheBluesland.SpotifyFetcher.CuratorNote;

/// <summary>
/// US-016/ADR-0005 madde 1: builds the AI prompt from exactly the four permitted
/// <see cref="SpotifyPlaylistCacheEntry"/> fields (name, description, track_count, artists) and
/// nothing else, even though the entry passed in carries every column. No track-level data,
/// cover image URL, or operational field (snapshot id, synced-at, availability) may reach the
/// prompt - see CuratorNotePromptBuilderTests for the regression test pinning that.
/// </summary>
public static class CuratorNotePromptBuilder
{
    public static string Build(SpotifyPlaylistCacheEntry entry)
    {
        var description = string.IsNullOrWhiteSpace(entry.Description)
            ? "(none provided)"
            : entry.Description;
        var artists = entry.Artists.Length > 0
            ? string.Join(", ", entry.Artists)
            : "(none listed)";

        return
            $"""
            You are drafting a short curator note for TheBluesland, a hand-curated Spotify
            playlist site with a warm, editorial, "late-night record room" voice - intimate and
            calm, never marketing copy.

            Playlist name: {entry.Name}
            Spotify's own description (may be empty or unhelpful): {description}
            Track count: {entry.TrackCount}
            Contributing artists: {artists}

            Write a curator note of 40-250 words, in one or two short paragraphs. Ground every
            claim only in the information above - do not invent facts about the playlist, its
            history, or its creator. No track-level data was provided, so never mention specific
            track titles or claim to have listened to particular songs; write about the playlist
            as a whole, its apparent theme, and the artists listed. Output only the note text
            itself - no heading, no title, no surrounding quotation marks.
            """;
    }
}
