using System.Text.Json;
using System.Text.RegularExpressions;

namespace JLNotes.Helpers;

/// <summary>
/// Outcome of "is a newer build published?". <see cref="Ok"/> false means the
/// check itself failed (offline, GitHub error, release without an installer);
/// <see cref="Error"/> says why. Never throws out of the parser.
/// </summary>
public sealed record UpdateCheckResult(
    bool Ok,
    Version Current,
    Version? Latest,
    bool UpToDate,
    string? AssetName,
    string? AssetUrl,
    string? Error)
{
    public static UpdateCheckResult Failed(Version current, string error) =>
        new(false, current, null, true, null, null, error);
}

/// <summary>
/// Pure, network-free half of the self-updater (ported from the MG Manager
/// Hub / A1i Connector pattern): version parsing and the GitHub
/// "releases/latest" JSON shape. Kept free of WPF and HTTP so it is unit-testable.
/// </summary>
public static class UpdateLogic
{
    /// <summary>Installer asset name = OutputBaseFilename in installer\JLNotes.iss.
    /// Must keep matching already-published assets.</summary>
    public static readonly Regex AssetRegex = new(@"^JLNotes-Setup-.*\.exe$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>"v1.2.7", "1.2.7", "1.2" -> Version(1.2.7 / 1.2.0). Null if unparseable.</summary>
    public static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.Trim();
        if (s.StartsWith('v') || s.StartsWith('V')) s = s[1..];
        // Drop any prerelease / build suffix ("1.2.7-rc1" -> "1.2.7").
        var cut = s.IndexOfAny(['-', '+', ' ']);
        if (cut >= 0) s = s[..cut];
        if (!Version.TryParse(s, out var v)) return null;
        // Normalize "1.2" and "1.2.7.0" to three components so comparisons are stable.
        return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
    }

    /// <summary>Parses a GitHub releases/latest response against the running version.</summary>
    public static UpdateCheckResult ParseLatestRelease(string json, Version current)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var latest = root.TryGetProperty("tag_name", out var tag) ? ParseVersion(tag.GetString()) : null;
            if (latest == null)
                return UpdateCheckResult.Failed(current, "release has no readable version tag");

            string? assetName = null, assetUrl = null;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (name == null || !AssetRegex.IsMatch(name)) continue;
                    assetName = name;
                    assetUrl = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                    break;
                }
            }
            if (assetName == null || string.IsNullOrEmpty(assetUrl))
                return UpdateCheckResult.Failed(current, "release has no installer asset");

            return new UpdateCheckResult(true, current, latest, current >= latest, assetName, assetUrl, null);
        }
        catch (JsonException)
        {
            return UpdateCheckResult.Failed(current, "unreadable release response");
        }
    }
}
