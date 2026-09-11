using System.Security.Cryptography;
using System.Text;

namespace TheBluesland.Web.Analytics;

/// <summary>
/// Privacy-preserving visitor hash (docs/specs/visitor-and-playlist-click-analytics.md, Design
/// section 3): <c>SHA256(pepper + utcDate + remoteIp + userAgent)</c>, hex-encoded. Deliberately
/// scoped to a single UTC calendar date so the same visitor hashes differently every day - this is
/// what makes "approximate daily unique visitors" (<c>COUNT(DISTINCT visitor_hash)</c> grouped by
/// date) derivable without ever storing anything that could correlate one visitor across two
/// different days. No raw IP address is ever stored; this is the only place one is read, and only
/// to feed this one-way hash. <paramref name="pepper"/> is a secret app-config value
/// (<c>Analytics:VisitorHashPepper</c>) so the hash cannot be brute-forced back to an IP by anyone
/// without it.
/// </summary>
public static class VisitorHashing
{
    private const string DateFormat = "yyyy-MM-dd";

    public static string Compute(string pepper, DateOnly utcDate, string remoteIp, string userAgent)
    {
        var input = pepper + utcDate.ToString(DateFormat) + remoteIp + userAgent;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(hash);
    }
}
