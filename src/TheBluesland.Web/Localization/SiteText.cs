namespace TheBluesland.Web.Localization;

/// <summary>
/// Every piece of site chrome that has a Turkish and an English wording (docs/specs/site-i18n.md,
/// "Strings approach"). A typed class instead of .resx: a missing translation is a compile error
/// (every property is <c>required</c> and both languages are object initialisers), there are no
/// satellite assemblies or string keys to mistype, and ICU/culture data is not needed. Long prose
/// with inline links (About, Privacy, Terms) lives next to its markup in the page instead, because
/// a link in the middle of a sentence does not fit a key/value string.
///
/// Instances: <see cref="English"/> (SiteText.En.cs) and <see cref="Turkish"/> (SiteText.Tr.cs).
/// </summary>
public sealed partial class SiteText
{
    public static SiteText For(string code) => code == SiteLanguage.Turkish ? Turkish : English;

    // Language switcher.
    public required string LanguageName { get; init; }
    public required string SwitcherLabel { get; init; }
    public required string SwitcherNoEditionTitle { get; init; }

    // Header and footer.
    public required string NavPrimaryLabel { get; init; }
    public required string NavPlaylists { get; init; }
    public required string NavCollections { get; init; }
    public required string NavWeekly { get; init; }
    public required string NavAbout { get; init; }
    public required string ThemeToLight { get; init; }
    public required string ThemeToDark { get; init; }
    public required string FooterNavLabel { get; init; }
    public required string FooterPrivacy { get; init; }
    public required string FooterTerms { get; init; }
    public required string FooterAttribution { get; init; }

    // Shared bits.
    public required string Home { get; init; }
    public required string BreadcrumbLabel { get; init; }
    public required string BrowseAllPlaylists { get; init; }
    public required Func<int, string> TrackCount { get; init; }
    public required Func<string, string> CoverAlt { get; init; }

    // Home: copy, search, filters, empty states.
    public required string HomeTitle { get; init; }
    public required string HomeDescription { get; init; }
    public required string HomeIntro { get; init; }
    public required string WeeklyStripLead { get; init; }
    public required string WeeklyStripText { get; init; }
    public required string SearchLabel { get; init; }
    public required string SearchPlaceholder { get; init; }
    public required string SearchButton { get; init; }
    public required string FiltersLabel { get; init; }
    public required string FiltersClose { get; init; }
    public required string FilterMood { get; init; }
    public required string FilterGenre { get; init; }
    public required string FilterOccasion { get; init; }
    public required string FilterEra { get; init; }
    public required string ApplyFilters { get; init; }
    public required string ClearFilters { get; init; }
    public required string ClearSearchAndFilters { get; init; }
    public required string PlaylistsHeading { get; init; }
    public required string NoResultsSearchAndFilters { get; init; }
    public required string NoResultsSearch { get; init; }
    public required string NoResultsFilters { get; init; }
    public required string NoPlaylistsYet { get; init; }
    public required string ShowMore { get; init; }

    // Playlist detail.
    public required string PlayerUnavailable { get; init; }
    public required string OpenInSpotify { get; init; }
    public required string RefreshMixHeading { get; init; }
    public required string RefreshMixIntro { get; init; }
    public required string CurrentlyExploring { get; init; }
    public required string MixModeNavLabel { get; init; }
    public required string ModeSameVibe { get; init; }
    public required string ModeMoreEnergetic { get; init; }
    public required string ModeRelaxedFlow { get; init; }
    public required string ModeDeeperCuts { get; init; }
    public required string ModeSameVibeHelp { get; init; }
    public required string ModeMoreEnergeticHelp { get; init; }
    public required string ModeRelaxedFlowHelp { get; init; }
    public required string ModeDeeperCutsHelp { get; init; }
    public required string MixEmpty { get; init; }
    public required Func<string, string> UseThisMixLabel { get; init; }
    public required string UseThisMix { get; init; }
    public required string KeepCurrentList { get; init; }
    public required string PreviewAgain { get; init; }
    public required string BackToCurrentPlaylist { get; init; }
    public required string RelatedPlaylists { get; init; }
    public required string FreshMixSelection { get; init; }

    // Sharing (the three Message* strings are also read by wwwroot/js/share.js through data attributes).
    public required string SharePlaylist { get; init; }
    public required string ShareViaDevice { get; init; }
    public required string CopyLink { get; init; }
    public required string ShareLinkLabel { get; init; }
    public required string ShareMessageSelect { get; init; }
    public required string ShareMessageCopied { get; init; }
    public required string ShareMessageUnavailable { get; init; }

    // Collections.
    public required string CollectionsTitle { get; init; }
    public required string CollectionsDescription { get; init; }
    public required string CollectionsHeading { get; init; }
    public required string CollectionsIntro { get; init; }
    public required string PlaylistsInCollectionLabel { get; init; }
    public required string ExploreOtherCollections { get; init; }

    // About.
    public required string AboutTitle { get; init; }
    public required string AboutDescription { get; init; }
    public required string AboutHeading { get; init; }
    public required string AboutFaqHeading { get; init; }
    public required string AboutCuratorDescription { get; init; }
    public required IReadOnlyList<(string Question, string Answer)> AboutFaq { get; init; }

    // Privacy and terms (the prose itself is in the pages).
    public required string PrivacyTitle { get; init; }
    public required string PrivacyDescription { get; init; }
    public required string TermsTitle { get; init; }
    public required string TermsDescription { get; init; }

    // Weekly hub, issue, thanks, subscribe box.
    public required string WeeklyTitle { get; init; }
    public required string WeeklyDescription { get; init; }
    public required string WeeklyIntro { get; init; }
    public required string WeeklyPastIssues { get; init; }
    public required string WeeklyFirstIssue { get; init; }
    public required string WeeklyFirstIssueSubscribe { get; init; }
    public required string WeeklyIssueWord { get; init; }
    public required string WeeklyIssueDescription { get; init; }
    public required string WeeklyMinutes { get; init; }
    public required string WeeklyOnlyInThisLanguage { get; init; }
    public required string WeeklyIssuesNavLabel { get; init; }
    public required string WeeklyPreviousIssue { get; init; }
    public required string WeeklyNextIssue { get; init; }
    public required string WeeklyJumpToSubscribe { get; init; }
    public required Func<int, string> WeeklyStories { get; init; }
    public required string WeeklyThanksTitle { get; init; }
    public required string WeeklyThanksBody { get; init; }
    public required string WeeklyBackToWeekly { get; init; }
    public required string SubscribeHeading { get; init; }
    public required string SubscribeIntro { get; init; }
    public required string SubscribeEmailLabel { get; init; }
    public required string SubscribeEmailPlaceholder { get; init; }
    public required string SubscribeTurkishButton { get; init; }
    public required string SubscribeEnglishButton { get; init; }
    public required string SubscribeNote { get; init; }
    public required string SubscribePrivacyLink { get; init; }
    public required string SubscribeSoon { get; init; }

    // Not found / error (each "body" is text before a link, the link text and text after it).
    public required string NotFoundTitle { get; init; }
    public required string NotFoundHeading { get; init; }
    public required string NotFoundBodyBefore { get; init; }
    public required string BrowseThePlaylistsLink { get; init; }
    public required string FailedTitle { get; init; }
    public required string FailedBodyBefore { get; init; }
    public required string WeeklyNotFoundTitle { get; init; }
    public required string WeeklyNotFoundHeading { get; init; }
    public required string WeeklyNotFoundBodyBefore { get; init; }
    public required string WeeklyNotFoundLink { get; init; }
    public required string PlaylistNotFoundTitle { get; init; }
    public required string PlaylistNotFoundBodyBefore { get; init; }
    public required string PlaylistNotFoundLink { get; init; }
    public required string PlaylistNotFoundBodyAfter { get; init; }
    public required string CollectionNotFoundTitle { get; init; }
    public required string CollectionNotFoundBodyBefore { get; init; }
    public required string CollectionNotFoundLink { get; init; }
    public required string CollectionNotFoundBodyAfter { get; init; }
}
