# Spec: Playlist mix refresh

Mehmet wants a “refresh/mix” experience for playlist browsing on thebluesland.com. This is not a full recommendation engine and not a redesign of the playlist catalog. It is a focused UI + logic enhancement that gives visitors a clear way to reframe the current playlist into a “similar but different” version without making the page feel heavy or unpredictable.

## Objective

Add a lightweight “Refresh mix” interaction to playlist pages that lets a visitor:

- re-roll the current playlist into a related but different mix,
- choose a mood or intensity variant,
- preview the next set before committing,
- keep the current experience fast and predictable on mobile and desktop.

The feature should feel like a small, high-value editorial tool rather than a generic AI recommendation panel.

## Problem

The site already presents curated playlists and related content well, but visitors do not have a clear way to say: “I like this, but I want a more energetic version,” or “Give me something with the same mood but a different selection.” Without that, the playlist experience can feel static and repetitive during longer browsing sessions.

## User stories

- “This playlist is great, but I want a more upbeat version.”
- “I want a reroll with the same mood, but not the same tracks.”
- “Show me a slightly deeper or more relaxed variant of this list.”
- “I want to preview a new mix before replacing the current list.”

## Scope

### In scope

- A visible refresh button on playlist pages.
- A small set of mix modes: similar mood, more energetic, more relaxed, deeper cuts.
- A preview result list that updates without replacing the current list immediately.
- Empty/loading/low-result states.
- Simple accessibility and responsive behavior.

### Out of scope

- Full personalized recommendations using user accounts
- Live AI-generated playlist curation across all music metadata
- Large backend recommendation pipeline
- Automatic mutation of editorial content without explicit user action
- User-level personalization or cross-visit tracking

## UX behavior

### Primary interaction

The visitor opens a playlist and sees a “Refresh mix” button near the list header or controls.

When they click it:

1. The site opens a compact action panel or inline controls.
2. The visitor chooses a mix mode.
3. The system generates a preview of the next mix from the same playlist’s metadata and content set.
4. The preview is shown as a list of suggested tracks or items.
5. The visitor can either:
   - keep the preview as a suggested alternate mix,
   - replace the current list,
   - dismiss the preview and return to the original list.

### Mix modes

Use a small, deterministic set of modes rather than a free-form prompt:

- Similar mood
- More energetic
- Relaxed flow
- Deeper cuts

Each mode should be represented by a short label and a one-line helper text.

Examples:

- Similar mood: “Keep the same atmosphere, swap in different tracks.”
- More energetic: “Go brighter, louder, and more immediate.”
- Relaxed flow: “Lower the energy, keep the mood warm and easy.”
- Deeper cuts: “Favor rarer, more niche selections from the same era and texture.”

## Functional requirements

### 1. Refresh entry point

- A visible trigger exists on playlist pages.
- The button is keyboard focusable and has clear text.
- The trigger remains visible on mobile and desktop.

### 2. Mix selection

- Visitor can choose one mix mode at a time.
- The selected mode changes the preview result.
- The selection state is clear via button styling and/or active state.

### 3. Preview generation

- The system uses the current playlist’s available metadata as the source set.
- It generates a preview from the same playlist context without mutating the original list immediately.
- The preview should avoid duplicate tracks already in the active list when possible.
- If the result set is too small, a graceful no-results state is shown.

### 4. Commit behavior

- The visitor can replace the current list with the preview.
- The current list remains intact until confirm action is taken.
- There should be a clear “cancel” or “dismiss” option.

### 5. State handling

The UI must handle:

- loading state during generation,
- empty result state,
- minor errors without breaking the page,
- a lightweight fallback if generation is unavailable.

Mix mode, selected playlist, the mode that produced an applied selection, and dismissed-preview
state are represented in the query string so they survive reloads and work with the site's static
server rendering. Previewing or dismissing another mode must not change the applied list. Other
query parameters and URL fragments must be preserved.

Mode previews, applied selections, and dismissed previews are counted in the existing
privacy-preserving analytics store. Only fixed action/mode event types, the playlist slug, and the
existing date-scoped visitor hash are stored; raw query values are not.

## Design requirements

### Layout

- Keep the feature compact and inline rather than page-breaking.
- Do not introduce heavy modal behavior unless the design truly requires it.
- Prefer a panel near the playlist header or controls.

### Copywriting

- Copy should be short and confidence-building.
- Example wording: “Refresh mix”, “Try a different mood”, “Preview mix”, “Keep current list”.

### Accessibility

- Buttons must have meaningful labels.
- Focus states must be visible.
- Screen readers must understand the active mode and result states.
- Contrast must remain acceptable in both dark and light themes.

## Technical approach

This project favors moderate, deterministic logic and minimal new infrastructure. The most suitable implementation path is:

- Use the existing playlist data and metadata stored in the app.
- Rank tracks based on a few editorial factors already available in the metadata model: era, mood, energy, track order, and similarity to the current list.
- Use deterministic mix presets rather than a large ML pipeline.
- Keep the recommendation logic in a small service or helper that can be unit-tested without creating a new platform abstraction.

This approach matches the project’s current style: prefer minimal complexity and observable behavior over a broad recommendation stack.

## Acceptance criteria

1. A visitor can open a playlist and see a refresh action.
2. Selecting a mix mode produces a visible preview result and stores the mode in the URL.
3. The current list is not replaced until the user explicitly confirms it; an applied selection
   survives a reload and only a playlist in the ranked related set can be applied.
4. Dismiss and restore actions survive reloads without dropping unrelated query parameters.
5. Loading and empty states are shown gracefully.
6. The interaction works on mobile and desktop forms without layout breakage.
7. Keyboard navigation and screen-reader semantics remain usable.
8. Mix states are counted using fixed analytics event types without storing raw query strings.
9. Ranking gives stronger weight to mood and genre overlap and uses exact taxonomy values for modes.
10. The feature does not require a new heavy dependency or custom infra.

## Test strategy

### UI validation

- Test that the refresh trigger renders on playlist pages.
- Test that choosing each preset changes the preview output.
- Test that canceling a preview returns to the original list.
- Test empty-result handling.
- Test query-string selection, application, dismissal, and restoration across reloads.
- Test that refresh controls are usable without client-side Blazor interactivity.
- Test that analytics accepts only known route modes and state values.

### Functional validation

- Ensure duplicate tracks are filtered or deprioritized in preview results.
- Ensure a result set is produced only when there is enough data.
- Ensure the original content remains stable until a user confirms.
- Ensure mode-specific ranking is deterministic and based on approved metadata.

### Manual QA

- Verify hover/focus/keyboard flow.
- Verify mobile layout on narrow viewports.
- Verify loading and error states under poor or incomplete data.

## Definition of done

The feature is complete when:

- the visitor can generate a previewed alternate mix from the current playlist,
- the result is understandable and legible,
- the original content is preserved until a confirmation action,
- the experience works across key form factors,
- the implementation remains within the project’s minimal-architecture bias.
