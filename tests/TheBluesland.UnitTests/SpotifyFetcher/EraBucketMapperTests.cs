using Shouldly;
using TheBluesland.SpotifyFetcher.EraReport;
using Xunit;

namespace TheBluesland.UnitTests.SpotifyFetcher;

/// <summary>
/// US-023: pins the year -> era-bucket boundaries to the same year ranges called out in the
/// story's spec ("&lt;1970, 1970-1979, 1980-1999, 2000+"), including every edge year explicitly.
/// </summary>
public sealed class EraBucketMapperTests
{
    [Theory]
    [InlineData(1969, EraBucketMapper.PreSeventies)]
    [InlineData(1970, EraBucketMapper.Seventies)]
    [InlineData(1979, EraBucketMapper.Seventies)]
    [InlineData(1980, EraBucketMapper.EightiesNineties)]
    [InlineData(1999, EraBucketMapper.EightiesNineties)]
    [InlineData(2000, EraBucketMapper.TwoThousandsPresent)]
    [InlineData(2026, EraBucketMapper.TwoThousandsPresent)]
    public void BucketForYear_maps_boundary_years_to_the_correct_bucket(int year, string expectedBucket)
    {
        EraBucketMapper.BucketForYear(year).ShouldBe(expectedBucket);
    }
}
