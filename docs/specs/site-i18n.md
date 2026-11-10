# Spec: Site localization (Turkish and English)

## Objective

Every page of thebluesland.com is available in Turkish (`/tr/...`) and English (`/en/...`), with the
interface chrome fully translated, a language switcher, and correct SEO signals. Editorial content
(playlist titles, summaries and curator notes, collection copy) stays English for now and is shown as
is on Turkish pages; the model has a place for Turkish text when it exists. Modelled on the owner's
SemtSkoru site (every page under `/[locale]/...`, one messages file per language, a locale switcher).

## URL scheme

- Every page lives under a language prefix: `/tr`, `/en`, `/{lang}/about`, `/{lang}/privacy`,
  `/{lang}/terms`, `/{lang}/collections`, `/{lang}/collections/{slug}`, `/{lang}/playlists/{slug}`,
  `/{lang}/weekly`, `/{lang}/weekly/thanks`, `/{lang}/weekly/{id}` (the issue language is the prefix).
  Slugs are identical in both languages; path words are not translated.
- Not prefixed, not redirected: `/sitemap.xml`, `/robots.txt`, `/llms.txt`, `/playlists.json`,
  `/og-image.png`, `/playlists/{slug}/og-image.png`, `/out/{slug}`, `/health/*`, `/dashboard`, static
  files, and `/not-found` (the page a 404 is re-executed to). Social cards are language-neutral.
- Implementation: each page declares two literal `@page` routes (`/tr/...`, `/en/...`); no route
  constraint, no catch-all, so no ambiguity with the minimal-API routes above.

## Redirects

| Request | Response |
| --- | --- |
| `/` (GET/HEAD) | 302 to `/tr` or `/en`: valid `lang` cookie, else the highest-quality `tr`/`en` entry of Accept-Language, else `/en`. Query string kept. `Vary: Accept-Language, Cookie`, `Cache-Control: no-store`. |
| `/about`, `/privacy`, `/terms`, `/collections`, `/collections/{slug}`, `/playlists/{slug}`, `/weekly` | 301 to the `/en` twin, query string kept (the site was English until now). |
| `/weekly/{id}/{tr\|en}` | 301 to `/{lang}/weekly/{ID}` (id upper-cased); any other language: 404. |
| `/weekly/{id}` (no language) | As before: 302 to the Turkish issue, or the English one when it is the only one published; lower-case id accepted; else 404. |
| `/weekly/thanks?lang=tr\|en` | 301 to `/tr/weekly/thanks` or `/en/weekly/thanks` (no or unknown `lang`: `/en`). Kit's forms still point at the old URL. |
| `/{lang}/weekly/{id}` with a lower-case id | 301 to the upper-case id. |
| `/{lang}/weekly/{id}` that exists only in the other language | 302 to the other language's issue (temporary: the translation may be published later). |
| Old playlist slug (`previousSlugs`) | 301 to `/{lang}/playlists/{new-slug}`, language kept. |

`lang` cookie: set (HttpOnly, SameSite=Lax, Path=/, one year, Secure on HTTPS) on a successful GET/HEAD
of a prefixed page when missing or different. The language switcher links to the prefixed page, so using
it is what saves the choice. The cookie is disclosed in the Privacy page.

## Strings approach

A typed class, not `.resx`: `Localization/SiteText.cs` declares every UI string as a `required`
property; `SiteText.En.cs` and `SiteText.Tr.cs` are object initialisers, so a missing translation does
not compile. No satellite assemblies, no string keys to mistype, no ICU dependency, trivially testable.
Components read `Site.T.<Name>` where `Site` is the scoped `SiteLanguage` (injected through
`_Imports.razor`), built per request from the URL prefix (or, for the unprefixed `/not-found` page, from
the original request of the status-code re-execution, then cookie, then Accept-Language).
Long prose with inline links (About, Privacy, Terms) sits next to its markup in an `@if (Site.IsTurkish)`
block, where a link in the middle of a sentence stays readable. Taxonomy labels (mood/genre/occasion/era)
live in `Localization/TaxonomyLabels.cs`: Turkish has a label per value, English shows the raw value as
before, an unknown value falls back to itself. Turkish search also matches the Turkish tag labels.
Client scripts (`share.js`, `theme.js`) read their messages from `data-*` attributes rendered per page.
Dates and numbers use built-in tables (`SiteLanguage.FormatDate/FormatNumber`) instead of
`CultureInfo("tr-TR")`, because the container may run with invariant globalization where that culture
cannot be created.

## Content fallback

`PlaylistContent` has optional `TitleTr`, `SummaryTr`, `CuratorNoteTr` (front matter `titleTr`,
`summaryTr`; the Turkish note is the part of the body after a line `<!-- lang:tr -->`). Nothing sets them
today. `TitleIn/SummaryIn/CuratorNoteIn(lang)` return the Turkish text when present, else the English text
plus `lang="en"` so the element is marked on a Turkish page. Cards, the detail view and the page
title/description use it; search, JSON-LD, social cards, the feed and `llms.txt` stay English. Collection
copy (defined in code) is English-only and marked `lang="en"` on Turkish pages; adding Turkish fields to
`PlaylistCollection` follows the same pattern when wanted. No machine translation.

## SEO

- `<html lang>` is the page language. Canonical is the page's own language URL (never the query string).
- Every page carries `hreflang` `tr`, `en` and `x-default` links; a page that exists in one language only
  (a weekly issue without a translation) lists just that one. `x-default` points at the English URL, not
  at the `/` redirector: a concrete content page needs no redirect hop, and crawlers send no
  Accept-Language, so `/` would resolve to English anyway.
- `og:locale` is `tr_TR`/`en_US` with the other as `og:locale:alternate`. JSON-LD gets `inLanguage`
  (page language for WebSite/AboutPage/FAQPage, `en` for playlist and collection pages).
- `/sitemap.xml` lists every page in both languages with `xhtml:link` alternates (`tr`, `en`, `x-default`).
  `robots.txt` is unchanged. `llms.txt` and `playlists.json` point at the `/en` URLs.
- The weekly e-mail renderer links to `/{lang}/weekly/{id}` and `/{lang}/weekly`.

## Testing

Integration tests (`SiteLocalizationTests`, `WeeklyArchiveTests`): redirects (`/`, legacy URLs, weekly,
thanks), non-page URLs unprefixed, every page in both languages (lang, canonical, hreflang, og:locale),
the switcher, the cookie, sitemap alternates, structured-data language, 404 in both languages. Unit tests
(`SiteLanguageTests`): prefix splitting, formatting, taxonomy labels, content fallback and reader.
Playwright smoke: updated paths plus a switcher flow.

## Out of scope

Translating editorial content or collection copy; localized path words; a third language; per-language
sitemaps; changing the social card images; hreflang for the weekly e-mail itself.
