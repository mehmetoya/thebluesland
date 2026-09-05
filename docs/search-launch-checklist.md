# Search setup before buying a domain

The current public origin is `https://thebluesland.onrender.com`. A custom domain is not required
for Google Search Console URL-prefix verification. Do not add an `onrender.com` Domain property:
we do not control Render's parent domain DNS.

## Google Search Console

Mehmet's supplied Google verification token is included in `src/TheBluesland.Web/appsettings.json`.
The account-side **Verify** and sitemap submission still require completion in Search Console.

1. Open https://search.google.com/search-console and add a **URL-prefix** property for
   `https://thebluesland.onrender.com/` in Mehmet's Google account.
2. Choose **HTML tag**. Keep only the `content` value of the supplied
   `google-site-verification` tag. Configure it as `SearchVerification__Google` in the web
   service environment, or as `SearchVerification:Google` in application configuration.
   This is a public verification token, not an account password. Do not use a sample token.
3. Deploy/restart, view the homepage source, and confirm the exact token is in `<head>`.
4. Click **Verify** in Search Console. Retain the token after verification; Google rechecks it.
5. Submit `https://thebluesland.onrender.com/sitemap.xml` under **Sitemaps**.
6. Inspect the homepage, `/collections`, the three collection pages, and several playlist URLs.
   Request indexing for representative pages. Submission is not a guarantee of indexing.
7. After data accumulates, review indexed pages, impressions, clicks and non-brand queries weekly.
   Search Console does not measure on-site Spotify-link clicks; no analytics tracker has been added.

Bing Webmaster Tools can use the optional `SearchVerification__Bing` value for its `msvalidate.01`
meta tag. Set it only to a token issued for this site and account.

## Shipped site preparation

- Server-rendered content, canonical HTTPS URLs, robots and XML sitemap.
- `/collections/anadolu-rock`, `/collections/blues`, `/collections/late-night`: named entry points
  into existing editorial tags, with distinct titles, descriptions and introductions.
- Navigation links through `/collections`; collection URLs included in the sitemap.
- Drafts remain inaccessible. Do not add every filter/query combination to the sitemap.
- No invented `lastmod` timestamps or search-ranking promises.

## First four weeks

Share one existing playlist per week with a short, personally written explanation of why it
belongs together. Link directly to its page. Use communities only where their sharing rules
permit it; avoid bulk promotion or purchased links. Review Search Console data before expanding
collection topics. No posts or messages have been sent as part of this setup.

## When a domain is purchased

Keep the existing URLs and slugs. Configure DNS and TLS, update `Site__PublicOrigin`, and implement
permanent old-origin redirects that preserve paths. Verify the new Search Console property,
submit its sitemap and follow Google's applicable migration process. Retain the old property
for comparison. Do not change the public origin before the new domain is actually serving HTTPS.

References:
- https://support.google.com/webmasters/answer/9008080
- https://developers.google.com/search/docs/crawling-indexing/sitemaps/build-sitemap
- https://developers.google.com/search/docs/crawling-indexing/site-move-with-url-changes
