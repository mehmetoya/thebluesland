using TheBluesland.WeeklyNewsletter.Drafting;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Drafting;

internal static partial class DraftingTestData
{
    /// <summary>
    /// The card the fallback-model draft of 2026-11-09 was written from, rebuilt from the W46 fixture
    /// articles plus the headlines and summary lines that run's own warnings quote: Lloyd, McCartney/Starr,
    /// Oasis, Del Casher and YouTube as picks; Sepultura, New Order and Tyler as backups.
    /// </summary>
    public static FactCard W47Card()
    {
        static FactArticle A(string source, int day, string title, string? summary = null) =>
            new(source, new DateTimeOffset(2026, 11, day, 12, 0, 0, TimeSpan.Zero), title, summary);

        static FactStory S(string key, int position, bool backup, params FactArticle[] articles) =>
            new(
                key,
                position,
                backup,
                "news",
                "test card",
                articles,
                articles.Select(article => article.SourceName).Distinct().Select(name => new SourceLink(name, $"https://example.test/{key}/{name.Replace(' ', '-')}")).ToList());

        return new FactCard(
        [
            S("s-20", 1, false,
                A("Pitchfork", 7, "Richard Lloyd, Television Guitarist, Dies at 75", "Lloyd co-founded the pioneering rock band alongside Tom Verlaine, Billy Ficca, and Richard Hell"),
                A("Stereogum", 7, "Television’s Richard Lloyd Dead At 75"),
                A("Ultimate Classic Rock", 7, "Richard Lloyd, Television Guitarist, Dead at 75"),
                A("The Guardian", 7, "Richard Lloyd, guitarist in punk band Television, dies aged 75"),
                A("Louder", 8, "Television guitarist Richard Lloyd dead at 75"),
                A("NME", 9, "Watch Geese pay tribute to Television’s Richard Lloyd with cover of ‘Ain’t That Nothin’", "Cameron Winter and co. have been compared to the NYC rockers before, and covered another one of their tracks back in 2024")),
            S("s-41", 2, false,
                A("Stereogum", 9, "Paul McCartney & Ringo Starr To Perform With Joe Walsh At VetsAid Benefit"),
                A("Ultimate Classic Rock", 9, "Paul McCartney and Ringo Starr to Reunite at VetsAid", "The star-studded benefit concert is headed to Las Vegas and also features The Killers, St. Vincent and The War on Drugs."),
                A("Consequence", 9, "Paul McCartney and Ringo Starr Will Reunite at VetsAid Benefit"),
                A("Rolling Stone", 9, "Paul McCartney and Ringo Starr to Reunite at Las Vegas Show in 2027", "The two Beatles will make a rare onstage appearance together at Joe Walsh's VetsAid music festival in January")),
            S("s-46", 3, false,
                A("Rolling Stone", 9, "Oasis Won’t Be Attending Rock and Roll of Fame Induction Ceremony", "After months of silence and the occasional glib X post, we’ve confirmed that Noel and Liam Gallagher won’t be in Los Angeles for the event on November 14"),
                A("Consequence", 9, "Oasis to Skip Rock and Roll Hall of Fame Induction Ceremony", "Despite finally making it into the Rock Hall, Noel and Liam Gallagher will not attend, perform, or accept the honor at the November 14th ceremony."),
                A("Stereogum", 9, "Oasis Won’t Attend Their Rock & Roll Hall Of Fame Induction")),
            S("s-8", 4, false,
                A("Ultimate Classic Rock", 9, "Del Casher, Inventor of the Wah-Wah Pedal, Dead at 88", "Casher also worked as a session guitarist for Elvis Presley and Sonny & Cher."),
                A("Billboard", 9, "Del Casher, Inventor of Guitar Wah-Wah Pedal Dies at 88", "Casher's invention was a favorite of Frank Zappa, Jimi Hendrix, Eric Clapton, Jimmy Page and Black Sabbath's Tony Iommi.")),
            S("s-13", 5, false,
                A("Music Business Worldwide", 9, "Billboard changes ‘free’ vs. paid streaming formula on charts – and YouTube is bringing its data back.", "Billboard announced the change on Monday (November 9), and YouTube confirmed its return later the same day."),
                A("Stereogum", 9, "YouTube Streams Will Once Again Count Toward Billboard Charts"),
                A("Pitchfork", 9, "YouTube Squashes Feud With Billboard Charts", "A year after the company said it would no longer submit its numbers for chart consideration, it’s reversed course")),
            S("s-77", 1, true,
                A("Louder", 9, "This is the epic setlist and all the surprise guests from Sepultura’s emotional final show", "Sepultura were joined by some old friends and new ones during their 30-song set at their last ever gig"),
                A("Stereogum", 9, "Sepultura Play Final Show At Massively Downgraded Venue"),
                A("Consequence", 9, "Sepultura End Farewell Tour with Final Concert in Brazil: Video + Setlist", "The 34-song set featured tunes from across the band's discography and a number of guests.")),
            S("s-15", 2, true,
                A("Consequence", 9, "New Order Announce 2027 US Tour Dates", "The band's first US tour since 2023 will feature support from Future Islands on select dates. New Order Announce 2027 US Tour Dates Paolo Ragusa"),
                A("Pitchfork", 9, "New Order Plot 2027 U.S. Tour", "Future Islands will support the synth-pop greats on three Texas dates to launch the Southern run"),
                A("Ultimate Classic Rock", 9, "New Order Announce 2027 U.S. Tour Dates")),
            S("s-42", 3, true,
                A("Rolling Stone", 9, "Judge Sets Steven Tyler Sex Assault Trial Date", "A California judge said Monday he would toss out and reconsider any rulings by a previous judge if either side requests it in the long-running case"),
                A("Louder", 9, "New date set for Steven Tyler sexual assault trial")),
        ]);
    }
}
