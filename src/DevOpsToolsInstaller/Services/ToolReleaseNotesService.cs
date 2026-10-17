using System.Net.Http;
using System.Text.Json;

namespace DevOpsToolsInstaller.Services;

public sealed record ToolReleaseNotes(
    string Repo,
    string TagName,
    string Body,
    string HtmlUrl);

/// <summary>
/// Fetches "what's new" release notes for a tool's newer version from the
/// vendor's GitHub Releases. Works for any tool whose download URL points at
/// a github.com releases page (roughly two thirds of the catalog); other
/// vendors are simply not offered the feature.
/// Results are cached per (repo, tag) for the session.
/// </summary>
public static class ToolReleaseNotesService
{
    private static readonly HttpClient Http;
    private static readonly Dictionary<string, ToolReleaseNotes?> Cache = new();

    static ToolReleaseNotesService()
    {
        Http = new HttpClient(HttpConfigService.CreateHandler())
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"DevOpsToolsInstaller/{AppUpdaterService.CurrentVersion} (Windows NT 10.0; Win64; x64)");
        Http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    /// <summary>
    /// Returns true when the tool's download URL points at a GitHub releases
    /// page, i.e. release notes can be offered for it.
    /// </summary>
    public static bool IsGitHubHosted(string downloadUrl)
        => TryParseRepo(downloadUrl) is not null;

    /// <summary>
    /// Fetches the release notes for the GitHub release the tool's download
    /// URL pins. Returns null when the vendor is not GitHub-hosted, the
    /// release could not be found, or the network failed.
    /// </summary>
    public static async Task<ToolReleaseNotes?> GetReleaseNotesAsync(string downloadUrl)
    {
        var repo = TryParseRepo(downloadUrl);
        if (repo is null)
        {
            return null;
        }

        var tag = TryParseTag(downloadUrl);
        var cacheKey = $"{repo.Value.owner}/{repo.Value.repo}@{tag ?? "latest"}";
        if (Cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        try
        {
            var apiUrl = tag is not null
                ? $"https://api.github.com/repos/{repo.Value.owner}/{repo.Value.repo}/releases/tags/{Uri.EscapeDataString(tag)}"
                : $"https://api.github.com/repos/{repo.Value.owner}/{repo.Value.repo}/releases/latest";

            using var response = await Http.GetAsync(apiUrl);
            if (!response.IsSuccessStatusCode)
            {
                Cache[cacheKey] = null;
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);

            var notes = new ToolReleaseNotes(
                Repo: $"{repo.Value.owner}/{repo.Value.repo}",
                TagName: doc.RootElement.TryGetProperty("tag_name", out var tagName) ? tagName.GetString() ?? "" : "",
                Body: doc.RootElement.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
                HtmlUrl: doc.RootElement.TryGetProperty("html_url", out var htmlUrl) ? htmlUrl.GetString() ?? "" : "");

            Cache[cacheKey] = notes;
            return notes;
        }
        catch
        {
            // Don't cache failures — a transient network error should retry.
            return null;
        }
    }

    /// <summary>
    /// Best-effort link to the vendor's release-notes page for the release the
    /// tool's download URL pins. Derived purely from the URL — no network call —
    /// so the detail pane can offer the link instantly, even offline.
    /// Returns null for vendors that are not GitHub-hosted.
    /// </summary>
    public static string? GetReleasesUrl(string downloadUrl)
    {
        var repo = TryParseRepo(downloadUrl);
        if (repo is null)
        {
            return null;
        }

        var tag = TryParseTag(downloadUrl);
        return tag is not null
            ? $"https://github.com/{repo.Value.owner}/{repo.Value.repo}/releases/tag/{Uri.EscapeDataString(tag)}"
            : $"https://github.com/{repo.Value.owner}/{repo.Value.repo}/releases/latest";
    }

    private static readonly Dictionary<string, string?> LicenseCache = new();

    /// <summary>
    /// Resolves the SPDX licence identifier of the tool's GitHub repository
    /// (e.g. "MIT", "Apache-2.0"). Returns null for vendors that are not
    /// GitHub-hosted, for repositories without a detected licence, or when the
    /// network failed. Cached per repository for the session.
    /// </summary>
    public static async Task<string?> GetRepositoryLicenseAsync(string downloadUrl)
    {
        var repo = TryParseRepo(downloadUrl);
        if (repo is null)
        {
            return null;
        }

        var key = $"{repo.Value.owner}/{repo.Value.repo}";
        if (LicenseCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        try
        {
            using var response = await Http.GetAsync($"https://api.github.com/repos/{repo.Value.owner}/{repo.Value.repo}");
            if (!response.IsSuccessStatusCode)
            {
                LicenseCache[key] = null;
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);

            string? spdx = null;
            if (doc.RootElement.TryGetProperty("license", out var license) &&
                license.ValueKind == JsonValueKind.Object &&
                license.TryGetProperty("spdx_id", out var spdxElement))
            {
                spdx = spdxElement.GetString();
                if (string.Equals(spdx, "NOASSERTION", StringComparison.OrdinalIgnoreCase))
                {
                    spdx = null;
                }
            }

            LicenseCache[key] = spdx;
            return spdx;
        }
        catch
        {
            // Don't cache failures — a transient network error should retry.
            return null;
        }
    }

    private static (string owner, string repo)? TryParseRepo(string url)
    {
        // Matches github.com/<owner>/<repo>/releases/...
        var match = System.Text.RegularExpressions.Regex.Match(
            url, @"github\.com/([^/]+)/([^/]+)/releases", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }
        return (match.Groups[1].Value, match.Groups[2].Value);
    }

    private static string? TryParseTag(string url)
    {
        // URLs pin tags: github.com/o/r/releases/download/v1.2.3/... or /releases/tag/v1.2.3
        var match = System.Text.RegularExpressions.Regex.Match(
            url, @"github\.com/[^/]+/[^/]+/releases/(?:download|tag)/([^/]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? Uri.UnescapeDataString(match.Groups[1].Value) : null;
    }
}
