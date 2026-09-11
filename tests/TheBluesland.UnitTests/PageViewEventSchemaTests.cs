using System.Reflection;
using Shouldly;
using TheBluesland.Data.Entities;
using Xunit;

namespace TheBluesland.UnitTests;

/// <summary>
/// docs/specs/visitor-and-playlist-click-analytics.md Testing Strategy: a schema test mirroring
/// SpotifyPlaylistCacheEntrySchemaTests.cs's pattern - <see cref="PageViewEvent"/> must expose
/// exactly the columns the spec's Design section 1 defines, and must never gain a raw IP field
/// (VisitorHash is the only visitor-identifying column, and it is a hash, never an address).
/// </summary>
public sealed class PageViewEventSchemaTests
{
    private static readonly string[] ExpectedPropertyNames =
    [
        nameof(PageViewEvent.Id),
        nameof(PageViewEvent.OccurredAt),
        nameof(PageViewEvent.EventType),
        nameof(PageViewEvent.Path),
        nameof(PageViewEvent.PlaylistSlug),
        nameof(PageViewEvent.VisitorHash),
    ];

    private static PropertyInfo[] GetDeclaredProperties() =>
        typeof(PageViewEvent).GetProperties(BindingFlags.Public | BindingFlags.Instance);

    [Fact]
    public void Entity_exposes_exactly_the_columns_defined_in_the_spec()
    {
        var actualPropertyNames = GetDeclaredProperties().Select(p => p.Name).ToArray();

        actualPropertyNames.ShouldBe(ExpectedPropertyNames, ignoreOrder: true);
    }

    [Fact]
    public void Entity_never_exposes_a_raw_ip_address_field()
    {
        var actualPropertyNames = GetDeclaredProperties().Select(p => p.Name).ToArray();

        actualPropertyNames.ShouldNotContain(
            name => name.Contains("Ip", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Address", StringComparison.OrdinalIgnoreCase),
            "entity must never expose a raw IP address field - only VisitorHash (spec Design section 3)");
    }

    [Fact]
    public void VisitorHash_is_a_string_hash_not_a_structured_address_type()
    {
        var visitorHashProperty = typeof(PageViewEvent).GetProperty(nameof(PageViewEvent.VisitorHash));

        visitorHashProperty.ShouldNotBeNull();
        visitorHashProperty.PropertyType.ShouldBe(typeof(string));
    }
}
