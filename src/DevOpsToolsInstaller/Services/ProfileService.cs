using System.Text.Json;
using DevOpsToolsInstaller.Models;

namespace DevOpsToolsInstaller.Services;

/// <summary>
/// Export and import tool selection profiles as JSON files.
/// A profile is simply a list of tool IDs that were selected.
/// </summary>
public static class ProfileService
{
    /// <summary>
    /// A serialized selection profile.
    /// </summary>
    public sealed class ToolProfile
    {
        public string Name { get; set; } = "Custom Profile";
        public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("o");
        public List<string> SelectedToolIds { get; set; } = new();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Exports the currently selected tools to a JSON string.
    /// </summary>
    public static string Export(IEnumerable<ToolDefinition> tools, string profileName = "Custom Profile")
    {
        var profile = new ToolProfile
        {
            Name = profileName,
            CreatedAt = DateTime.UtcNow.ToString("o"),
            SelectedToolIds = tools
                .Where(t => t.IsSelected)
                .Select(t => t.Id)
                .ToList()
        };

        return JsonSerializer.Serialize(profile, JsonOptions);
    }

    /// <summary>
    /// Imports a profile from a JSON string and applies the selection to the tool list.
    /// Returns the number of tools that were selected.
    /// </summary>
    public static int Import(string json, IEnumerable<ToolDefinition> tools)
    {
        var profile = JsonSerializer.Deserialize<ToolProfile>(json, JsonOptions);
        if (profile is null || profile.SelectedToolIds.Count == 0)
            return 0;

        var ids = new HashSet<string>(profile.SelectedToolIds, StringComparer.OrdinalIgnoreCase);
        int selected = 0;

        foreach (var tool in tools)
        {
            if (ids.Contains(tool.Id))
            {
                tool.IsSelected = true;
                selected++;
            }
        }

        return selected;
    }
}
