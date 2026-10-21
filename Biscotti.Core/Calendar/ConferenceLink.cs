using System.Text.RegularExpressions;

namespace Biscotti.Core.Calendar;

/// <summary>Only recognized HTTPS conference links are actionable.</summary>
public static partial class ConferenceLink
{
    [GeneratedRegex("https://[^\\s<>\"']+", RegexOptions.IgnoreCase, 100)]
    private static partial Regex Links();

    public static Uri? Find(params string?[] values)
    {
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            foreach (Match match in Links().Matches(value))
            {
                if (Uri.TryCreate(match.Value.TrimEnd('.', ',', ';', ')', ']', '}'), UriKind.Absolute, out var uri)
                    && Platform(uri) is not null) return uri;
            }
        }
        return null;
    }

    public static string? Platform(Uri? uri)
    {
        if (uri is null || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length > 0
            || uri.AbsolutePath.Length <= 1) return null;
        var host = uri.IdnHost.ToLowerInvariant();
        if (host == "meet.google.com") return "Google Meet";
        if (host is "teams.microsoft.com" or "teams.live.com" or "teams.cloud.microsoft") return "Teams";
        if (host == "zoom.us" || host.EndsWith(".zoom.us", StringComparison.Ordinal)
            || host == "zoom.com" || host.EndsWith(".zoom.com", StringComparison.Ordinal)) return "Zoom";
        if (host.EndsWith(".webex.com", StringComparison.Ordinal)) return "Webex";
        return null;
    }
}
