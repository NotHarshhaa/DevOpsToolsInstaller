using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsToolsInstaller.Services;

/// <summary>
/// One GitHub release offered as an installable version of a tool.
/// </summary>
/// <param name="Version">Clean segment version derived from the tag ("1.9.5").</param>
/// <param name="TagName">The raw release tag, e.g. "v1.9.5".</param>
/// <param name="PublishedAt">When GitHub published the release (null when unknown).</param>
/// <param name="IsPrerelease">True when the vendor marked the release as a pre-release.</param>
/// <param name="WindowsAssetUrl">Direct URL of the release asset that matches this machine's
/// OS/architecture, when one could be identified (null falls back to the catalog's
/// URL-templating rules).</param>
/// <param name="WindowsAssetName">File name of that asset.</param>
public sealed record ToolReleaseVersion(
    string Version,
    string TagName,
    DateTimeOffset? PublishedAt,
    bool IsPrerelease,
    string? WindowsAssetUrl,
    string? WindowsAssetName);

/// <summary>
/// A vendor that serves its catalog download from its own CDN but publishes
/// the same Windows binaries as GitHub Release assets. <see cref="AssetHint"/>
/// biases asset selection toward the vendor's file naming (e.g. the
/// installer/setup variant among several Windows binaries); null means the
/// best-scoring Windows asset is used as-is.
/// </summary>
/// <param name="Owner">GitHub repository owner.</param>
/// <param name="Repo">GitHub repository name.</param>
/// <param name="AssetHint">Preferred asset-name fragment, or null.</param>
internal sealed record FallbackRepo(string Owner, string Repo, string? AssetHint);

/// <summary>
/// Live version history for GitHub-hosted catalog tools. The newest
/// <see cref="MaxVersions"/> releases of a tool's repository are queried from
/// the GitHub Releases API so users can install an older version that still
/// meets their requirements — the catalog's <c>previousVersions</c> list is
/// curated but goes stale, while this reflects what the vendor actually
/// published. Results are cached per repository for
/// <see cref="CacheLifetime"/>; failures are never cached so network hiccups
/// retry on the next open.
/// </summary>
public static class ToolVersionHistoryService
{
    /// <summary>How many recent releases are offered per tool.</summary>
    public const int MaxVersions = 5;

    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Verified vendor-CDN → GitHub-releases mirrors: these tools' downloadable
    /// Windows artifacts ship as release assets even though their catalog URL
    /// points at a vendor CDN. Extend this table (with verification) only for
    /// repos that genuinely attach the binaries — a wrong entry would offer
    /// versions the vendor has never shipped for Windows.
    /// </summary>
    private static readonly Dictionary<string, FallbackRepo> FallbackRepos =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["helm"] = new FallbackRepo("helm", "helm", null),
            ["kind"] = new FallbackRepo("kubernetes-sigs", "kind", null),
            ["minikube"] = new FallbackRepo("kubernetes-sigs", "minikube", "installer"),
            ["dbeaver"] = new FallbackRepo("dbeaver", "dbeaver", "setup"),
        };

    private static readonly HttpClient Http;

    static ToolVersionHistoryService()
    {
        Http = new HttpClient(HttpConfigService.CreateHandler())
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"DevOpsToolsInstaller/{AppUpdaterService.CurrentVersion} (Windows NT 10.0; Win64; x64)");
        Http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    private static readonly ConcurrentDictionary<string, (DateTimeOffset FetchedAt, IReadOnlyList<ToolReleaseVersion> Releases)> Cache = new();

    /// <summary>
    /// True when live version history can be fetched for the tool: either its
    /// download URL points at GitHub Releases, or the tool id is listed in
    /// <see cref="FallbackRepos"/> as a vendor whose binaries are mirrored to
    /// GitHub releases.
    /// </summary>
    public static bool CanFetchVersionHistory(string toolId, string downloadUrl)
        => ToolReleaseNotesService.GetGitHubRepo(downloadUrl) is not null
           || (!string.IsNullOrWhiteSpace(toolId) && FallbackRepos.ContainsKey(toolId));

    /// <summary>
    /// Fetches the newest <see cref="MaxVersions"/> releases of the repository
    /// the tool's download URL points at (or a known GitHub mirror of the
    /// vendor's releases, per <see cref="FallbackRepos"/>), newest first.
    /// Returns null when the tool has no known GitHub releases source or the
    /// request failed — the caller should then fall back to the catalog's
    /// curated version list.
    /// </summary>
    public static async Task<IReadOnlyList<ToolReleaseVersion>?> GetRecentReleasesAsync(
        string toolId, string downloadUrl, CancellationToken ct = default)
    {
        var repo = ToolReleaseNotesService.GetGitHubRepo(downloadUrl);
        if (repo is not null)
        {
            return await GetReleasesForRepoAsync(
                $"{repo.Value.Owner}/{repo.Value.Repo}", assetHint: null, ct);
        }

        if (string.IsNullOrWhiteSpace(toolId) ||
            !FallbackRepos.TryGetValue(toolId, out var fallback))
        {
            return null;
        }

        return await GetReleasesForRepoAsync(
            $"{fallback.Owner}/{fallback.Repo}", fallback.AssetHint, ct);
    }

    private static async Task<IReadOnlyList<ToolReleaseVersion>?> GetReleasesForRepoAsync(
        string repoKey, string? assetHint, CancellationToken ct)
    {
        if (Cache.TryGetValue(repoKey, out var cached) &&
            DateTimeOffset.UtcNow - cached.FetchedAt < CacheLifetime)
        {
            return cached.Releases;
        }

        try
        {
            using var response = await Http.GetAsync(
                $"https://api.github.com/repos/{repoKey}/releases?per_page={MaxVersions}", ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            var releases = new List<ToolReleaseVersion>();
            foreach (var rel in doc.RootElement.EnumerateArray())
            {
                var tagName = rel.TryGetProperty("tag_name", out var tagProp)
                    ? tagProp.GetString() ?? ""
                    : "";
                if (string.IsNullOrWhiteSpace(tagName)) continue;

                // Drafts are not user-installable releases.
                if (rel.TryGetProperty("draft", out var draftProp) &&
                    draftProp.ValueKind == JsonValueKind.True)
                {
                    continue;
                }

                DateTimeOffset? publishedAt = null;
                if (rel.TryGetProperty("published_at", out var pubProp) &&
                    pubProp.ValueKind == JsonValueKind.String &&
                    pubProp.TryGetDateTimeOffset(out var published))
                {
                    publishedAt = published;
                }

                var isPrerelease = rel.TryGetProperty("prerelease", out var preProp) &&
                    preProp.ValueKind == JsonValueKind.True;

                var (assetUrl, assetName) = PickWindowsAsset(rel, assetHint);

                releases.Add(new ToolReleaseVersion(
                    Version: AppUpdaterService.CleanVersionString(tagName),
                    TagName: tagName,
                    PublishedAt: publishedAt,
                    IsPrerelease: isPrerelease,
                    WindowsAssetUrl: assetUrl,
                    WindowsAssetName: assetName));

                if (releases.Count >= MaxVersions) break;
            }

            Cache[repoKey] = (DateTimeOffset.UtcNow, releases);
            return releases;
        }
        catch
        {
            // Never cache failures — a transient network error must retry.
            return null;
        }
    }

    /// <summary>
    /// Picks the release asset that matches this machine: Windows plus the
    /// running architecture, preferring native installers/archives over tar
    /// balls. Tools that ship a platform-neutral Windows binary (e.g.
    /// <c>kubectl.exe</c>) still score well. Returns (null, null) when no
    /// convincing Windows asset exists, in which case the catalog's existing
    /// URL-templating rules build the download URL.
    /// </summary>
    private static (string? Url, string? Name) PickWindowsAsset(JsonElement release, string? assetHint)
    {
        if (!release.TryGetProperty("assets", out var assets) ||
            assets.ValueKind != JsonValueKind.Array)
        {
            return (null, null);
        }

        var isArm = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;

        (string Name, string Url, int Score)? best = null;
        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? "" : "";
            var url = asset.TryGetProperty("browser_download_url", out var uProp) ? uProp.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url)) continue;

            var score = ScoreAsset(name, isArm, assetHint);
            if (score < 0) continue;
            if (best is null || score > best.Value.Score)
                best = (name, url, score);
        }

        return best is null ? (null, null) : (best.Value.Url, best.Value.Name);
    }

    /// <summary>Negative score excludes the asset; higher is a better match.</summary>
    private static int ScoreAsset(string name, bool isArm, string? assetHint)
    {
        var n = name.ToLowerInvariant();

        var isNativeWinInstaller = n.EndsWith(".exe") || n.EndsWith(".msi");
        if (!n.Contains("win") && !isNativeWinInstaller) return -1;

        // Guard against cross-platform archives that merely contain "x64".
        if (n.Contains("darwin") || n.Contains("macos") || n.Contains("apple") ||
            n.Contains("linux") || n.Contains("android") || n.Contains("solaris"))
        {
            return -1;
        }

        bool archOk = isArm
            ? n.Contains("arm64") || n.Contains("aarch64")
            : n.Contains("amd64") || n.Contains("x64") ||
              n.Contains("x86_64") || n.Contains("x86-64") || n.Contains("86_64");
        bool archExplicitlyOther = isArm
            ? !n.Contains("arm64") && !n.Contains("aarch64")
            : n.Contains("arm64");

        var score = 0;
        if (archOk) score += 4;
        else if (!archExplicitlyOther) score += 2;

        // Platform-neutral Windows binaries (kubectl.exe) rank between a
        // matching archive and a fully-qualified installer.
        if (isNativeWinInstaller && !n.Contains("win"))
        {
            score += 3;
        }
        else if (n.EndsWith(".exe") || n.EndsWith(".msi"))
        {
            score += 5;
        }
        else if (n.EndsWith(".zip"))
        {
            score += 4;
        }
        else if (n.EndsWith(".tar.gz") || n.EndsWith(".tgz"))
        {
            score += 2;
        }

        // Vendor hints (FallbackRepos.AssetHint) steer selection to the
        // artifact the vendor actually distributes for this tool, e.g. the
        // standalone installer among several Windows binaries.
        if (!string.IsNullOrEmpty(assetHint) && n.Contains(assetHint))
        {
            score += 6;
        }

        return score;
    }
}
