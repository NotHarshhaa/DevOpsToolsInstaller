using System.Text.Json;

namespace DevOpsToolsInstaller.Services;

/// <summary>
/// Checks the GitHub Releases API on startup for a newer version of the app.
/// </summary>
public static class UpdateCheckerService
{
    private const string CurrentVersion = "2.1.0";
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
            "DevOpsToolsInstaller/2.1.0 (Windows NT 10.0; Win64; x64)");
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
        var release = await AppUpdaterService.CheckForUpdatesAsync(ct);
        if (release == null) return null;

        return new UpdateInfo(
            IsUpdateAvailable: release.IsUpdateAvailable,
            LatestVersion: release.Version,
            ReleaseUrl: release.HtmlUrl,
            ReleaseNotes: release.Body.Length > 200 ? release.Body[..200] + "…" : release.Body
        );
    }
}
