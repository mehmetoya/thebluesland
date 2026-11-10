using Shouldly;
using TheBluesland.UnitTests.WeeklyNewsletter.Clustering;
using TheBluesland.WeeklyNewsletter.Delivery;
using Xunit;
using static TheBluesland.UnitTests.WeeklyNewsletter.Delivery.DeliveryTestData;

namespace TheBluesland.UnitTests.WeeklyNewsletter.Delivery;

/// <summary>issue-delivery spec, Design section 5.</summary>
public sealed class DeliveryOptionsLoaderTests
{
    [Fact]
    public void A_complete_file_loads()
    {
        var options = DeliveryOptionsLoader.Parse(ValidYaml).Options.ShouldNotBeNull();

        options.MinimumLead.ShouldBe(TimeSpan.FromMinutes(2));
        options.KitPublic.ShouldBeFalse();
        options.Languages["tr"].ShouldBe(new LanguageDelivery(new TimeOnly(8, 0), 111));
        options.Languages["en"].ShouldBe(new LanguageDelivery(new TimeOnly(15, 0), 222));
    }

    [Fact]
    public void The_repository_file_is_valid_apart_from_tag_ids_waiting_for_the_kit_setup()
    {
        var result = DeliveryOptionsLoader.Parse(File.ReadAllText(RepoFiles.Find("tools/weekly-newsletter/delivery.yml")));

        result.Errors.ShouldAllBe(error => error.Contains("kitTagId is not set", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("timeZone: Europe/Istanbul", "timeZone: Mars/Olympus", "not a known time zone")]
    [InlineData("sendTime: \"08:00\"", "sendTime: \"8am\"", "languages.tr.sendTime must be HH:mm")]
    [InlineData("kitTagId: 111", "kitTagId: null", "languages.tr.kitTagId is not set")]
    [InlineData("minimumLeadMinutes: 2", "minimumLeadMinutes: -1", "minimumLeadMinutes must be zero")]
    [InlineData("  en:\n", "  de:\n", "languages.de is not an issue language")]
    public void Every_problem_is_named(string from, string to, string expected)
    {
        var result = DeliveryOptionsLoader.Parse(ValidYaml.Replace(from, to, StringComparison.Ordinal));

        result.Options.ShouldBeNull();
        result.Errors.ShouldContain(error => error.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_keys_are_errors()
    {
        DeliveryOptionsLoader.Parse(ValidYaml + "\nsendNow: true\n").Errors.ShouldHaveSingleItem().ShouldContain("not valid YAML");
    }
}
