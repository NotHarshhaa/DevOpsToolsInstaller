using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevOpsToolsInstaller.Models;

namespace DevOpsToolsInstaller.Services;

public static class CustomStacksService
{
    private static readonly string StorageFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DevOpsToolsInstaller", "custom_stacks.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static List<ToolBundle> LoadCustomStacks()
    {
        try
        {
            if (!File.Exists(StorageFilePath))
                return new List<ToolBundle>();

            var json = File.ReadAllText(StorageFilePath);
            var list = JsonSerializer.Deserialize<List<ToolBundle>>(json, JsonOptions);
            return list ?? new List<ToolBundle>();
        }
        catch
        {
            return new List<ToolBundle>();
        }
    }

    public static void SaveCustomStack(ToolBundle stack)
    {
        try
        {
            var dir = Path.GetDirectoryName(StorageFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var all = LoadCustomStacks();
            var existingIndex = all.FindIndex(b =>
                string.Equals(b.Id, stack.Id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(b.Name, stack.Name, StringComparison.OrdinalIgnoreCase));

            if (existingIndex >= 0)
            {
                all[existingIndex] = stack;
            }
            else
            {
                all.Add(stack);
            }

            var json = JsonSerializer.Serialize(all, JsonOptions);
            File.WriteAllText(StorageFilePath, json);
        }
        catch
        {
            // Best effort persistence
        }
    }

    public static void DeleteCustomStack(string stackId)
    {
        try
        {
            var all = LoadCustomStacks();
            all.RemoveAll(b => string.Equals(b.Id, stackId, StringComparison.OrdinalIgnoreCase));
            var json = JsonSerializer.Serialize(all, JsonOptions);
            File.WriteAllText(StorageFilePath, json);
        }
        catch { }
    }

    public static string ExportToJson(ToolBundle stack)
    {
        return JsonSerializer.Serialize(stack, JsonOptions);
    }

    public static (ToolBundle? Stack, List<string> UnknownToolIds, string? ErrorMessage) ImportFromJson(
        string json, IReadOnlyList<ToolDefinition> catalogTools)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return (null, new List<string>(), "JSON file is empty.");
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            // If an array was exported, pick the first item
            if (root.ValueKind == JsonValueKind.Array)
            {
                var array = root.EnumerateArray().ToList();
                if (array.Count == 0)
                {
                    return (null, new List<string>(), "JSON array contains no stacks.");
                }
                root = array[0];
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, new List<string>(), "JSON must contain a stack object.");
            }

            string name = "Imported Stack";
            if (root.TryGetProperty("name", out var nameProp))
                name = nameProp.GetString() ?? name;

            string description = string.Empty;
            if (root.TryGetProperty("description", out var descProp))
                description = descProp.GetString() ?? string.Empty;

            string id = "custom-" + Guid.NewGuid().ToString("N")[..8];
            if (root.TryGetProperty("id", out var idProp) && !string.IsNullOrWhiteSpace(idProp.GetString()))
                id = idProp.GetString()!;

            string category = "Custom";
            if (root.TryGetProperty("category", out var catProp) && !string.IsNullOrWhiteSpace(catProp.GetString()))
                category = catProp.GetString()!;

            string icon = "\uE71D";
            if (root.TryGetProperty("icon", out var iconProp) && !string.IsNullOrWhiteSpace(iconProp.GetString()))
                icon = iconProp.GetString()!;

            var rawToolIds = new List<string>();
            if (root.TryGetProperty("tools", out var toolsProp) && toolsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in toolsProp.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.String)
                    {
                        var s = el.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) rawToolIds.Add(s);
                    }
                    else if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("id", out var innerId))
                    {
                        var s = innerId.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) rawToolIds.Add(s);
                    }
                }
            }

            var catalogLookup = catalogTools.ToDictionary(t => t.Id, t => t, StringComparer.OrdinalIgnoreCase);
            var knownTools = new List<string>();
            var unknownIds = new List<string>();

            foreach (var rawId in rawToolIds)
            {
                if (catalogLookup.ContainsKey(rawId))
                {
                    if (!knownTools.Contains(rawId, StringComparer.OrdinalIgnoreCase))
                    {
                        knownTools.Add(rawId);
                    }
                }
                else
                {
                    if (!unknownIds.Contains(rawId, StringComparer.OrdinalIgnoreCase))
                    {
                        unknownIds.Add(rawId);
                    }
                }
            }

            var bundle = new ToolBundle
            {
                Id = id,
                Name = name,
                Description = description,
                Category = category,
                Icon = icon,
                Tools = knownTools
            };

            return (bundle, unknownIds, null);
        }
        catch (Exception ex)
        {
            return (null, new List<string>(), $"Invalid stack JSON format: {ex.Message}");
        }
    }

    public static long EstimateDownloadSizeBytes(IEnumerable<string> toolIds, IReadOnlyList<ToolDefinition> catalogTools)
    {
        var dlFolder = DownloadService.DefaultDownloadsFolder;
        var lookup = catalogTools.ToDictionary(t => t.Id, t => t, StringComparer.OrdinalIgnoreCase);

        long total = 0;
        foreach (var tid in toolIds)
        {
            if (!lookup.TryGetValue(tid, out var tool)) continue;

            // Check if already downloaded on disk
            try
            {
                var filePath = Path.Combine(dlFolder, tool.FileName);
                if (File.Exists(filePath))
                {
                    var len = new FileInfo(filePath).Length;
                    if (len > 0)
                    {
                        total += len;
                        continue;
                    }
                }
            }
            catch { }

            // Heuristic estimate based on kind / known tools
            total += EstimateToolSize(tool);
        }

        return total;
    }

    private static long EstimateToolSize(ToolDefinition tool)
    {
        // Special case known large tools
        var id = tool.Id.ToLowerInvariant();
        if (id.Contains("docker-desktop")) return 580L * 1024 * 1024;
        if (id.Contains("gcloud")) return 160L * 1024 * 1024;
        if (id.Contains("awscli")) return 140L * 1024 * 1024;
        if (id.Contains("azure-cli")) return 110L * 1024 * 1024;
        if (id.Contains("minikube")) return 95L * 1024 * 1024;
        if (id.Contains("git")) return 52L * 1024 * 1024;
        if (id.Contains("powershell") || id.Contains("pwsh")) return 95L * 1024 * 1024;
        if (id.Contains("terraform")) return 38L * 1024 * 1024;
        if (id.Contains("kubectl")) return 48L * 1024 * 1024;
        if (id.Contains("helm")) return 18L * 1024 * 1024;

        return tool.Kind switch
        {
            ArtifactKind.Installer => 75L * 1024 * 1024,
            ArtifactKind.Archive   => 35L * 1024 * 1024,
            ArtifactKind.Binary    => 25L * 1024 * 1024,
            _                      => 15L * 1024 * 1024
        };
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 MB";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }
}
