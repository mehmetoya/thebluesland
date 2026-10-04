# Spec: Playlist search by style and mood

## Objective

Help visitors find a published playlist that fits a style or mood they describe in their own
words. This extends catalogue-wide discovery beyond the related-playlist paths on detail pages.
Success means a visitor can enter a style/mood query, find a relevant result, and open it.

## User and scope

- The user is a visitor browsing TheBluesland's playlist catalogue.
- Search is available on the home page, before the playlist list, alongside existing filters.
- Search uses published playlist titles, summaries, and editorial mood, genre, occasion, and era
  tags. It does not call AI or an external service.
- Search terms combine with selected taxonomy filters; each active filter still applies.
- Partial matches are shown and ordered by the number of distinct query terms matched. Equal
  scores retain the existing catalogue order.
- A plain GET form carries the search and filter state in the URL; pagination preserves the query.
- An empty query leaves current catalogue behavior unchanged. An unmatched search has a clear,
  accessible no-results message and a way to clear search and filters.

## Tech stack

.NET 10 / C# 14, Blazor Web App with static server-side rendering, ASP.NET Core query binding,
and the existing xUnit/Shouldly test project. No new packages, database changes, or APIs.

## Commands

- Build: `dotnet build TheBluesland.slnx --no-restore`
- Focused tests: `dotnet test tests/TheBluesland.UnitTests/TheBluesland.UnitTests.csproj --no-restore --filter "FullyQualifiedName~PlaylistSearchTests|FullyQualifiedName~HomePageSearchIntegrationTests"`
- Full tests: `dotnet test TheBluesland.slnx --no-restore`
- Local app: `dotnet run --project src/TheBluesland.Web`

## Project structure

- `src/TheBluesland.Web/Content`: pure catalogue search/ranking logic.
- `src/TheBluesland.Web/Components/Pages/HomePage.razor`: GET search form and server-rendered
  result list integrated with existing filters and pagination.
- `src/TheBluesland.Web/Styles/app.css`: responsive search-field styling using existing tokens.
- `tests/TheBluesland.UnitTests/Web`: unit and in-process HTTP integration coverage.
- `docs/specs/playlist-search.md`: this feature's specification.

## Code style

Follow the existing pure-function content helpers and static SSR patterns:

```csharp
public static IReadOnlyList<PlaylistContent> Apply(
    IReadOnlyList<PlaylistContent> playlists,
    string? query)
{
    // Keep the catalogue order when no search terms are present.
}
```

Use explicit input/output types, deterministic ordering, native form semantics, and existing CSS
tokens. Do not introduce client-side interactivity for this feature.

## Testing strategy

- Unit tests cover blank queries, matching title/summary/taxonomy values, case/punctuation
  handling, partial-term ranking, and stable ties.
- In-process HTTP tests verify the home-page search field, query-bound results, composition with
  existing filters, empty-state behavior, and query preservation in the "Show more" link.
- Build and full solution tests run before delivery. Browser verification checks keyboard-usable
  submission and responsive layout at narrow and desktop widths.

## Boundaries

- Always: preserve existing filter behavior and catalogue tie order; encode search in the GET URL;
  provide an accessible label and a useful no-results state.
- Ask first: adding packages, external/AI search, database changes, indexing infrastructure, or
  recording raw search queries.
- Never: add user-level personalization, artist/track similarity, client-only search that fails
  under static SSR, or silent match-all fallbacks for non-empty queries.

## Success criteria

- A visitor can submit a style/mood phrase from the home page without JavaScript.
- Matching uses playlist title, summary, and editorial taxonomy tags.
- Partial matches are ranked by query-term coverage; ties preserve catalogue order.
- Existing taxonomy filters compose with search, and "Show more" retains the search query.
- Empty search preserves the unfiltered catalogue; no-match search shows a clear empty state.
- Unit/integration tests and the solution build pass; no new dependency is introduced.

## Open questions

None. Search placement, supported content, partial-match behavior, and the no-AI boundary were
confirmed during the interview.
