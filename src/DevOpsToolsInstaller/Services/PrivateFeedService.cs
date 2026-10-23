using System.Collections.Concurrent;
using System.Text.Json;
using DevOpsToolsInstaller.Models;

namespace DevOpsToolsInstaller.Services;

public sealed record FeedSyncResult(CatalogFeed Feed, bool Success, int ToolsLoaded, string Message);

/// <summary>
/// Manages subscriptions to private and corporate catalog feeds (HTTPS URLs or network shares).
/// Merges approved tools and specific versions into the application catalog.
/// </summary>
public static class PrivateFeedService
{
    private static readonly string StorageFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DevOpsToolsInstaller");

    private static readonly string FeedsFilePath = Path.Combine(StorageFolder, "feeds.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static readonly HttpClient Http;
    private static readonly object Gate = new();

    static PrivateFeedService()
    {
        Http = new HttpClient(HttpConfigService.CreateHandler())
        {
            Timeout = TimeSpan.FromSeconds(25)
        };
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"DevOpsToolsInstaller/{AppUpdaterService.CurrentVersion} (Windows NT 10.0; Win64; x64; EnterpriseFeed)");
    }

    public static List<CatalogFeed> LoadFeeds()
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(FeedsFilePath)) return new List<CatalogFeed>();
                var json = File.ReadAllText(FeedsFilePath);
                var list = JsonSerializer.Deserialize<List<CatalogFeed>>(json, JsonOptions) ?? new List<CatalogFeed>();

                // Merge in any enterprise policy enforced feeds that might not be saved yet
                var policyFeeds = EnterprisePolicyService.GetEnforcedFeeds();
                foreach (var pf in policyFeeds)
                {
                    if (!list.Any(f => string.Equals(f.Url, pf.Url, StringComparison.OrdinalIgnoreCase)))
                    {
                        pf.IsPolicyEnforced = true;
                        list.Add(pf);
                    }
                }

                return list;
            }
            catch (Exception ex)
            {
                ActivityLogService.Warn("Feeds", $"Error loading feeds: {ex.Message}");
                return new List<CatalogFeed>();
            }
        }
    }

    public static void SaveFeeds(List<CatalogFeed> feeds)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(StorageFolder);
                File.WriteAllText(FeedsFilePath, JsonSerializer.Serialize(feeds, JsonOptions));
            }
            catch (Exception ex)
            {
                ActivityLogService.Error("Feeds", $"Error saving feeds: {ex.Message}");
            }
        }
    }

    public static async Task<CatalogFeed> AddOrUpdateFeedAsync(string name, string url, bool isEnabled = true)
    {
        if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("Feed URL or path cannot be empty.", nameof(url));
        var trimmedUrl = url.Trim();
        var trimmedName = string.IsNullOrWhiteSpace(name) ? GetDefaultNameFromUrl(trimmedUrl) : name.Trim();

        var feeds = LoadFeeds();
        var existing = feeds.FirstOrDefault(f => string.Equals(f.Url, trimmedUrl, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Name = trimmedName;
            existing.IsEnabled = isEnabled;
            SaveFeeds(feeds);
            await SyncFeedAsync(existing);
            return existing;
        }

        var newFeed = new CatalogFeed
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = trimmedName,
            Url = trimmedUrl,
            IsEnabled = isEnabled
        };

        feeds.Add(newFeed);
        SaveFeeds(feeds);
        await SyncFeedAsync(newFeed);
        return newFeed;
    }

    public static bool RemoveFeed(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier)) return false;
        var feeds = LoadFeeds();
        var target = feeds.FirstOrDefault(f =>
            string.Equals(f.Id, identifier, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(f.Name, identifier, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(f.Url, identifier, StringComparison.OrdinalIgnoreCase));

        if (target is null) return false;
        if (target.IsPolicyEnforced)
        {
            ActivityLogService.Warn("Feeds", $"Feed '{target.Name}' is locked by enterprise policy and cannot be removed.");
            return false;
        }

        feeds.Remove(target);
        SaveFeeds(feeds);

        // Remove local cached tools file for this feed
        var cachePath = GetFeedCachePath(target.Id);
        try { if (File.Exists(cachePath)) File.Delete(cachePath); } catch { }

        ActivityLogService.Info("Feeds", $"Unsubscribed from feed '{target.Name}'.");
        return true;
    }

    public static async Task<FeedSyncResult> SyncFeedAsync(CatalogFeed feed, CancellationToken ct = default)
    {
        if (!feed.IsEnabled)
        {
            return new FeedSyncResult(feed, false, 0, "Feed is disabled.");
        }

        try
        {
            string json;
            var src = feed.Url.Trim();

            if (Uri.TryCreate(src, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                // Enforce enterprise policy source restrictions if enabled
                if (!EnterprisePolicyService.IsUrlAllowed(src))
                {
                    throw new InvalidOperationException($"Feed URL '{src}' is blocked by enterprise catalog source policy.");
                }

                json = await Http.GetStringAsync(src, ct);
            }
            else if (File.Exists(src))
            {
                json = await File.ReadAllTextAsync(src, ct);
            }
            else
            {
                throw new FileNotFoundException($"Catalog feed source file or network path '{src}' was not found.");
            }

            var tools = ParseFeedJson(json);
            if (tools.Count == 0)
            {
                throw new FormatException("Feed returned an empty or invalid catalog list.");
            }

            // Tag each tool with the feed identity
            foreach (var t in tools)
            {
                t.FeedName = feed.Name;
            }

            // Cache tools locally for offline runs
            var cachePath = GetFeedCachePath(feed.Id);
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            await File.WriteAllTextAsync(cachePath, json, ct);

            feed.LastSyncedUtc = DateTime.UtcNow;
            feed.ToolCount = tools.Count;
            feed.SyncError = null;

            // Update in stored feeds
            var all = LoadFeeds();
            var idx = all.FindIndex(f => f.Id == feed.Id);
            if (idx >= 0) { all[idx] = feed; SaveFeeds(all); }

            ActivityLogService.Info("Feeds", $"Feed '{feed.Name}' synchronized successfully ({tools.Count} tools).");
            return new FeedSyncResult(feed, true, tools.Count, $"Successfully synced {tools.Count} tools.");
        }
        catch (Exception ex)
        {
            feed.SyncError = ex.Message;
            var all = LoadFeeds();
            var idx = all.FindIndex(f => f.Id == feed.Id);
            if (idx >= 0) { all[idx] = feed; SaveFeeds(all); }

            ActivityLogService.Error("Feeds", $"Sync failed for feed '{feed.Name}': {ex.Message}");
            return new FeedSyncResult(feed, false, 0, ex.Message);
        }
    }

    public static async Task<List<FeedSyncResult>> SyncAllFeedsAsync(CancellationToken ct = default)
    {
        var feeds = LoadFeeds().Where(f => f.IsEnabled).ToList();
        var results = new List<FeedSyncResult>();
        foreach (var feed in feeds)
        {
            results.Add(await SyncFeedAsync(feed, ct));
        }
        return results;
    }

    /// <summary>
    /// Loads cached tools from all enabled private feeds.
    /// </summary>
    public static List<ToolDefinition> LoadCachedFeedTools()
    {
        var result = new List<ToolDefinition>();
        var feeds = LoadFeeds().Where(f => f.IsEnabled).ToList();

        foreach (var feed in feeds)
        {
            var cachePath = GetFeedCachePath(feed.Id);
            if (!File.Exists(cachePath)) continue;

            try
            {
                var json = File.ReadAllText(cachePath);
                var tools = ParseFeedJson(json);
                if (tools.Count > 0)
                {
                    foreach (var t in tools)
                    {
                        t.FeedName = feed.Name;
                    }
                    result.AddRange(tools);
                }
            }
            catch (Exception ex)
            {
                ActivityLogService.Warn("Feeds", $"Could not read cache for feed '{feed.Name}': {ex.Message}");
            }
        }

        return result;
    }

    /// <summary>
    /// Merges corporate and private feed tools into the base catalog.
    /// Company feed entries take precedence over public catalog entries if IDs collide
    /// (ensuring company-approved versions, custom internal URLs, and hashes are enforced).
    /// </summary>
    public static List<ToolDefinition> MergeFeedTools(List<ToolDefinition> baseCatalog)
    {
        var feedTools = LoadCachedFeedTools();
        if (feedTools.Count == 0) return baseCatalog;

        var merged = new List<ToolDefinition>(baseCatalog);
        var baseMap = merged.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var ft in feedTools)
        {
            if (baseMap.TryGetValue(ft.Id, out var existing))
            {
                // Replace or overlay with corporate-approved tool definition
                var index = merged.IndexOf(existing);
                if (index >= 0)
                {
                    ft.FeedName = ft.FeedName ?? "Company Approved";
                    // Preserve running detection and installed status
                    ft.IsInstalled = existing.IsInstalled;
                    ft.DetectedVersion = existing.DetectedVersion;
                    ft.Status = existing.Status;
                    ft.Progress = existing.Progress;
                    ft.SelectedBackend = existing.SelectedBackend;

                    merged[index] = ft;
                    baseMap[ft.Id] = ft;
                }
            }
            else
            {
                // Internal or proprietary tool
                ft.FeedName = ft.FeedName ?? "Company Approved";
                merged.Add(ft);
                baseMap[ft.Id] = ft;
            }
        }

        return merged;
    }

    private static List<ToolDefinition> ParseFeedJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                return JsonSerializer.Deserialize<List<ToolDefinition>>(json, JsonOptions) ?? new List<ToolDefinition>();
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in new[] { "tools", "items", "catalog", "definitions" })
                {
                    if (root.TryGetProperty(prop, out var listEl) && listEl.ValueKind == JsonValueKind.Array)
                    {
                        return JsonSerializer.Deserialize<List<ToolDefinition>>(listEl.GetRawText(), JsonOptions) ?? new List<ToolDefinition>();
                    }
                }
            }
        }
        catch { }

        return JsonSerializer.Deserialize<List<ToolDefinition>>(json, JsonOptions) ?? new List<ToolDefinition>();
    }

    private static string GetFeedCachePath(string feedId) =>
        Path.Combine(StorageFolder, "Feeds", $"feed_cache_{feedId}.json");

    private static string GetDefaultNameFromUrl(string url)
    {
        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return uri.Host;
            }
            return Path.GetFileNameWithoutExtension(url);
        }
        catch
        {
            return "Private Feed";
        }
    }
}
