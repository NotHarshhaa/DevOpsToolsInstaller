using System.Reflection;
using System.Text.Json;
using DevOpsToolsInstaller.Models;

namespace DevOpsToolsInstaller.Services;

public sealed class CatalogService
{
    private const string RemoteCatalogUrl =
        "https://raw.githubusercontent.com/NotHarshhaa/DevOpsToolsInstaller/main/catalog/catalog.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly HttpClient Http;

    static CatalogService()
    {
        Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "DevOpsToolsInstaller/1.5.0 (Windows NT 10.0; Win64; x64)");
    }

    /// <summary>
    /// Loads the tool catalog. Tries the remote GitHub URL first;
    /// falls back to the embedded Assets/catalog.json if offline.
    /// </summary>
    public async Task<List<ToolDefinition>> LoadCatalogAsync(CancellationToken ct = default)
    {
        // 1. Try remote
        try
        {
            var json = await Http.GetStringAsync(RemoteCatalogUrl, ct);
            var tools = JsonSerializer.Deserialize<List<ToolDefinition>>(json, JsonOptions);
            if (tools is { Count: > 0 })
                return tools;
        }
        catch
        {
            // Network unavailable — fall through to embedded copy
        }

        // 2. Embedded fallback
        return LoadEmbeddedCatalog();
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
