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

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

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
    /// </summary>
    private static List<ToolDefinition> LoadEmbeddedCatalog()
    {
        var exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var catalogPath = Path.Combine(exeDir, "Assets", "catalog.json");

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
