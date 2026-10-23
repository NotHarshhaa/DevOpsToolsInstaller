using System.Reflection;
using System.Text;
using System.Text.Json;
using DevOpsToolsInstaller.Models;

namespace DevOpsToolsInstaller.Services;

public sealed class CatalogService
{
    private const string RemoteCatalogUrl =
        "https://raw.githubusercontent.com/NotHarshhaa/DevOpsToolsInstaller/HEAD/catalog/catalog.json";

    private const string RemoteBundlesUrl =
        "https://raw.githubusercontent.com/NotHarshhaa/DevOpsToolsInstaller/HEAD/catalog/bundles.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly HttpClient Http;

    /// <summary>
    /// Human-readable description of where the active catalog was loaded from
    /// and how stale it is, set during the last catalog load. Surfaced in the
    /// UI so "Newer than catalog" states are explainable.
    /// </summary>
    public static string CatalogFreshness { get; private set; } = "Catalog source not loaded yet";

    static CatalogService()
    {
        Http = new HttpClient(HttpConfigService.CreateHandler())
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"DevOpsToolsInstaller/{AppUpdaterService.CurrentVersion} (Windows NT 10.0; Win64; x64)");
    }

    /// <summary>
    /// Loads the tool catalog, then folds in any user-defined custom tools from
    /// the in-app catalog editor. Uses the remote GitHub copy only when its
    /// signature verifies against the pinned public key; otherwise falls back
    /// to the embedded Assets/catalog.json (fail closed against tampering).
    /// </summary>
    public async Task<List<ToolDefinition>> LoadCatalogAsync(CancellationToken ct = default)
    {
        var tools = await LoadBaseCatalogAsync(ct);
        var customMerged = CustomCatalogService.Merge(tools);
        var feedMerged = PrivateFeedService.MergeFeedTools(customMerged);
        return feedMerged;
    }

    private async Task<List<ToolDefinition>> LoadBaseCatalogAsync(CancellationToken ct)
    {
        // 0. Check for custom catalog source (Settings -> Updates)
        if (!string.IsNullOrWhiteSpace(SettingsService.CustomCatalogSource))
        {
            try
            {
                var src = SettingsService.CustomCatalogSource.Trim();
                string json;
                if (Uri.TryCreate(src, UriKind.Absolute, out var customUri) &&
                    (customUri.Scheme == Uri.UriSchemeHttp || customUri.Scheme == Uri.UriSchemeHttps))
                {
                    json = await Http.GetStringAsync(src, ct);
                }
                else if (File.Exists(src))
                {
                    json = await File.ReadAllTextAsync(src, ct);
                }
                else
                {
                    throw new FileNotFoundException($"Custom catalog source '{src}' was not found.");
                }

                var customTools = JsonSerializer.Deserialize<List<ToolDefinition>>(json, JsonOptions);
                if (customTools is { Count: > 0 })
                {
                    CatalogFreshness = "custom catalog source";
                    ActivityLogService.Info("Catalog", $"Loaded {customTools.Count} tools from custom catalog source.");
                    return customTools;
                }
            }
            catch (Exception ex)
            {
                ActivityLogService.Warn("Catalog", $"Could not load custom catalog feed ({ex.Message}) — falling back to standard catalog.");
            }
        }

        // 1. Try remote (signature-verified)
        try
        {
            var json = await Http.GetStringAsync(RemoteCatalogUrl, ct);
            if (await VerifyRemoteSignatureAsync(RemoteCatalogUrl, json, ct))
            {
                var tools = JsonSerializer.Deserialize<List<ToolDefinition>>(json, JsonOptions);
                if (tools is { Count: > 0 })
                {
                    CatalogFreshness = "live from GitHub (signature verified)";
                    return tools;
                }
            }
            else
            {
                ActivityLogService.Warn(
                    "Catalog", "Remote catalog signature missing or invalid — using the built-in catalog.");
            }
        }
        catch
        {
            ActivityLogService.Info(
                "Catalog", "Remote catalog unreachable — using the built-in catalog.");
        }

        // 2. Embedded fallback (baked into the app at build time)
        var embeddedPath = Path.Combine(AppContext.BaseDirectory, "Assets", "catalog.json");
        CatalogFreshness = DescribeEmbeddedCatalogAge(embeddedPath);
        return LoadEmbeddedCatalog();
    }

    /// <summary>
    /// Describes the age of the embedded catalog snapshot using the file's
    /// last-write time, e.g. "built-in snapshot (2026-10-15, 2 days old)".
    /// </summary>
    private static string DescribeEmbeddedCatalogAge(string catalogPath)
    {
        try
        {
            if (!File.Exists(catalogPath)) return "built-in snapshot";

            var written = File.GetLastWriteTimeUtc(catalogPath);
            var days = (int)(DateTime.UtcNow - written).TotalDays;
            var when = written.ToString("yyyy-MM-dd");
            return days <= 0
                ? $"built-in snapshot ({when})"
                : $"built-in snapshot ({when}, {days} day{(days == 1 ? string.Empty : "s")} old)";
        }
        catch
        {
            return "built-in snapshot";
        }
    }

    /// <summary>
    /// Loads curated tool bundles. Same fail-closed signature policy as the catalog.
    /// </summary>
    public async Task<List<ToolBundle>> LoadBundlesAsync(CancellationToken ct = default)
    {
        // 1. Try remote (signature-verified)
        try
        {
            var json = await Http.GetStringAsync(RemoteBundlesUrl, ct);
            if (await VerifyRemoteSignatureAsync(RemoteBundlesUrl, json, ct))
            {
                var bundles = JsonSerializer.Deserialize<List<ToolBundle>>(json, JsonOptions);
                if (bundles is { Count: > 0 })
                    return bundles;
            }
            else
            {
                ActivityLogService.Warn(
                    "Catalog", "Remote bundles signature missing or invalid — using the built-in stacks.");
            }
        }
        catch
        {
            ActivityLogService.Info(
                "Catalog", "Remote bundles unreachable — using the built-in stacks.");
        }

        // 2. Embedded fallback (baked into the app at build time)
        return LoadEmbeddedBundles();
    }

    /// <summary>
    /// Fetches the base64 ECDSA signature (&lt;url&gt;.sig) for a remote catalog
    /// file and verifies it against the pinned public key over the exact bytes
    /// served for the file.
    /// </summary>
    private static async Task<bool> VerifyRemoteSignatureAsync(
        string fileUrl, string content, CancellationToken ct)
    {
        try
        {
            var signature = await Http.GetStringAsync(fileUrl + ".sig", ct);
            return CatalogSignatureService.Verify(Encoding.UTF8.GetBytes(content), signature.Trim());
        }
        catch
        {
            // Missing signature file (404) or fetch failure counts as unsigned.
            return false;
        }
    }

    /// <summary>
    /// Loads the catalog that was copied into the output directory at build time.
    /// Uses AppContext.BaseDirectory so single-file deployments can locate the file.
    /// </summary>
    private static List<ToolDefinition> LoadEmbeddedCatalog()
    {
        var catalogPath = Path.Combine(AppContext.BaseDirectory, "Assets", "catalog.json");

        if (!File.Exists(catalogPath))
            return new List<ToolDefinition>();

        var json = File.ReadAllText(catalogPath);
        return JsonSerializer.Deserialize<List<ToolDefinition>>(json, JsonOptions)
               ?? new List<ToolDefinition>();
    }

    /// <summary>
    /// Loads the curated bundles copied into the output directory at build time.
    /// </summary>
    private static List<ToolBundle> LoadEmbeddedBundles()
    {
        var bundlesPath = Path.Combine(AppContext.BaseDirectory, "Assets", "bundles.json");

        if (!File.Exists(bundlesPath))
            return new List<ToolBundle>();

        var json = File.ReadAllText(bundlesPath);
        return JsonSerializer.Deserialize<List<ToolBundle>>(json, JsonOptions)
               ?? new List<ToolBundle>();
    }

    /// <summary>
    /// Groups tools by category in a stable order.
    /// </summary>
    public static IEnumerable<IGrouping<string, ToolDefinition>> GroupByCategory(
        IEnumerable<ToolDefinition> tools)
    {
        return tools
            .GroupBy(t => t.Category)
            .OrderBy(g => g.Key);
    }
}
