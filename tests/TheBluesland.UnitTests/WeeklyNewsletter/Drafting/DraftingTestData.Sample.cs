using TheBluesland.WeeklyNewsletter.Drafting;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Drafting;

internal static partial class DraftingTestData
{
    /// <summary>
    /// The fictional fact card behind the voice guide's sample issue (every artist, place and event is
    /// invented). The sample is mapped onto it by position, so the calibration test stays meaningful:
    /// the validator must pass the sample against the card it was written from, and nothing in the
    /// sample may rest on a real story.
    /// </summary>
    public static FactCard SampleCard()
    {
        static FactArticle A(string source, int day, string title, string? summary = null) =>
            new(source, new DateTimeOffset(2026, 11, day, 12, 0, 0, TimeSpan.Zero), title, summary);

        static FactStory S(string key, int position, params FactArticle[] articles) =>
            new(
                key,
                position,
                false,
                "news",
                "sample card",
                articles,
                articles.Select(article => article.SourceName).Distinct().Select(name => new SourceLink(name, $"https://example.test/{key}/{name.Replace(' ', '-')}")).ToList());

        return new FactCard(
        [
            S("s-901", 1,
                A("Needle & Groove", 6, "Tessaly Brand, Orchard Kids Bassist, Dies at 68", "Brand played on all six of the band's albums, alongside Ione Marchetti and Dale Kerrigan."),
                A("Harbour Press", 6, "The Orchard Kids Bassist Tessaly Brand Has Died", "The Port Alder band confirmed her death at 68 in a statement."),
                A("Wireless Weekly", 7, "The Orchard Kids Play \"Low Tide Lullaby\" at Tessaly Brand Memorial", "Marchetti and Kerrigan performed the song at a memorial show in Port Alder.")),
            S("s-902", 2,
                A("Needle & Groove", 8, "Velvet Harbour Skip Calder Vale Prize Ceremony", "The act is shortlisted for album of the year and told organisers it would not turn up to the Calder Vale Music Prize on November 12."),
                A("Harbour Press", 8, "Velvet Harbour Pull Out of Calder Vale Music Prize Night", "Dunmore Hall Records, the group's label, said Hallvik was where a show had been booked that night."),
                A("Wireless Weekly", 8, "Velvet Harbour Won’t Attend Calder Vale Music Prize", "The shortlist for album of the year has five acts.")),
            S("s-903", 3,
                A("Harbour Press", 4, "Calder Vale Chart to Count Venue Vinyl Sales From January 16", "From January 16 the weekly album chart will include vinyl bought at the gig itself."),
                A("Needle & Groove", 4, "Calder Vale Album Chart Adds Gig Vinyl to Its Count", "Web orders placed after the show stay outside the count, the chart company said. The first chart under the new rule comes out on January 23.")),
            S("s-904", 4,
                A("Needle & Groove", 5, "Brindle Court Singer Maren Sloane Announces Solo Album \"Cold Frames\"", "The album is due on February 20, with the single \"Mothlight\" out now."),
                A("Harbour Press", 5, "Maren Sloane Details First Solo Album, Shares \"Mothlight\"", "The songs were written in a gap between Brindle Court tours, and the group has not announced a pause."),
                A("Wireless Weekly", 5, "Brindle Court’s Maren Sloane Plans Solo Record \"Cold Frames\"", "Recording took place at Ash Row Studios in Calder Vale.")),
            S("s-905", 5,
                A("Harbour Press", 3, "Port Alder’s Saltwater Ballroom to Reopen on December 5", "The venue closed after a fire in March. Ticket sales for opening night start on November 20."),
                A("Needle & Groove", 3, "Saltwater Ballroom Sets Reopening Date After Fire", "The 900-capacity club will reopen with a show by Sable Quarter.")),
        ]);
    }
}
