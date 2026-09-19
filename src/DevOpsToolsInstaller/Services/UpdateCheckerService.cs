using System.Text.Json;

namespace DevOpsToolsInstaller.Services;

/// <summary>
/// Checks the GitHub Releases API on startup for a newer version of the app.
/// </summary>
public static class UpdateCheckerService
{
    private const string CurrentVersion = "1.5.0";
    private const string GitHubReleasesUrl =
        "https://api.github.com/repos/NotHarshhaa/DevOpsToolsInstaller/releases/latest";

    private static readonly HttpClient Http;

    static UpdateCheckerService()
    {
        Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "DevOpsToolsInstaller/1.5.0 (Windows NT 10.0; Win64; x64)");
        Http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github.v3+json");
    }

    /// <summary>
    /// Result of an update check.
    /// </summary>
    public sealed record UpdateInfo(
        bool IsUpdateAvailable,
        string LatestVersion,
        string ReleaseUrl,
        string ReleaseNotes);

    /// <summary>
    /// Checks GitHub for a newer release. Returns null on any failure (offline, etc.).
    /// </summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await Http.GetStringAsync(GitHubReleasesUrl, ct);
            using var doc = JsonDocument.Parse(json);

            var tagName = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            var htmlUrl = doc.RootElement.GetProperty("html_url").GetString() ?? "";
            var body = doc.RootElement.TryGetProperty("body", out var bodyProp)
                ? bodyProp.GetString() ?? ""
                : "";

            // Strip leading 'v' for comparison
            var latestVersion = tagName.TrimStart('v', 'V');

            if (IsNewerVersion(latestVersion, CurrentVersion))
            {
                return new UpdateInfo(
                    IsUpdateAvailable: true,
                    LatestVersion: latestVersion,
                    ReleaseUrl: htmlUrl,
                    ReleaseNotes: body.Length > 200 ? body[..200] + "…" : body);
            }

            return new UpdateInfo(false, CurrentVersion, "", "");
        }
        catch
        {
            // Network error, parse error, etc. — silently return null
            return null;
        }
    }

    /// <summary>
    /// Simple semantic version comparison (major.minor.patch).
    /// Returns true if <paramref name="latest"/> is strictly newer than <paramref name="current"/>.
    /// </summary>
    private static bool IsNewerVersion(string latest, string current)
    {
        if (Version.TryParse(latest, out var vLatest) &&
            Version.TryParse(current, out var vCurrent))
        {
            return vLatest > vCurrent;
        }
        return false;
    }
}
