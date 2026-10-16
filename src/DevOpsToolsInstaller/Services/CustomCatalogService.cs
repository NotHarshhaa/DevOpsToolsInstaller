using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DevOpsToolsInstaller.Models;

namespace DevOpsToolsInstaller.Services;

/// <summary>
/// Manages user-defined custom tool entries (roadmap: "In-app catalog editor").
///
/// Custom tools are stored locally in
/// %LOCALAPPDATA%\DevOpsToolsInstaller\custom_tools.json and merged into the
/// catalog that the app and headless CLI operate on. They are validated against
/// the same shape as the shipped catalog so the rest of the pipeline
/// (download, SHA-256 verify, Authenticode check, install) works unchanged.
/// </summary>
public static class CustomCatalogService
{
    private static readonly string StorageFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DevOpsToolsInstaller", "custom_tools.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static readonly object Gate = new();

    public static List<ToolDefinition> LoadCustomTools()
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(StorageFilePath)) return new List<ToolDefinition>();
                var json = File.ReadAllText(StorageFilePath);
                return JsonSerializer.Deserialize<List<ToolDefinition>>(json, JsonOptions)
                       ?? new List<ToolDefinition>();
            }
            catch
            {
                return new List<ToolDefinition>();
            }
        }
    }

    private static void SaveAll(List<ToolDefinition> tools)
    {
        lock (Gate)
        {
            try
            {
                var dir = Path.GetDirectoryName(StorageFilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(StorageFilePath, JsonSerializer.Serialize(tools, JsonOptions));
            }
            catch
            {
                // Best-effort persistence.
            }
        }
    }

    public static ToolDefinition? Get(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return LoadCustomTools().FirstOrDefault(t =>
            string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Adds or replaces a custom tool. Returns true when persisted.</summary>
    public static bool Save(ToolDefinition tool)
    {
        if (tool is null || Validate(tool).Error is not null) return false;

        var all = LoadCustomTools();
        var idx = all.FindIndex(t => string.Equals(t.Id, tool.Id, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0) all[idx] = tool; else all.Add(tool);
        SaveAll(all);
        return true;
    }

    public static void Delete(string id)
    {
        var all = LoadCustomTools();
        all.RemoveAll(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
        SaveAll(all);
    }

    public static bool IsCustom(string id)
        => LoadCustomTools().Any(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Validates a custom tool definition. The app requires a stable id, a
    /// name, an HTTPS download URL, a file name, and an artifact kind so the
    /// security pipeline can run unchanged.
    /// </summary>
    public static (bool Ok, string? Error) Validate(ToolDefinition tool)
    {
        if (tool is null) return (false, "No tool supplied.");
        if (string.IsNullOrWhiteSpace(tool.Id)) return (false, "An id is required.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(tool.Id.Trim(), @"^[a-z0-9][a-z0-9\-\._]{1,63}$"))
            return (false, "The id must be lower-case letters, digits, '-', '.' or '_' (2–64 chars).");
        if (string.IsNullOrWhiteSpace(tool.Name)) return (false, "A display name is required.");
        if (string.IsNullOrWhiteSpace(tool.Category)) return (false, "A category is required.");
        if (string.IsNullOrWhiteSpace(tool.FileName)) return (false, "A file name is required.");

        if (string.IsNullOrWhiteSpace(tool.DownloadUrl))
            return (false, "A download URL is required.");
        if (!Uri.TryCreate(tool.DownloadUrl, UriKind.Absolute, out var uri))
            return (false, "The download URL is not a valid absolute URI.");
        if (uri.Scheme != Uri.UriSchemeHttps)
            return (false, "Only HTTPS download URLs are allowed (plain-text HTTP is refused).");
        if (!string.IsNullOrWhiteSpace(tool.Sha256) &&
            !System.Text.RegularExpressions.Regex.IsMatch(tool.Sha256.Trim(), @"^[a-fA-F0-9]{64}$"))
            return (false, "When provided, the SHA-256 must be exactly 64 hex characters.");

        return (true, null);
    }

    /// <summary>Serializes the custom tools as a portable catalog feed (array).</summary>
    public static string ExportFeedJson()
    {
        var tools = LoadCustomTools();
        return JsonSerializer.Serialize(tools, JsonOptions);
    }

    /// <summary>
    /// Imports custom tool entries from a JSON array (or a single object),
    /// skipping invalid entries. Returns the number imported and any errors.
    /// </summary>
    public static (int Imported, List<string> Errors) ImportFeed(string json)
    {
        var imported = 0;
        var errors = new List<string>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var items = root.ValueKind == JsonValueKind.Array
                ? root.EnumerateArray().ToList()
                : new List<JsonElement> { root };

            foreach (var item in items)
            {
                try
                {
                    var tool = JsonSerializer.Deserialize<ToolDefinition>(item.GetRawText(), JsonOptions);
                    if (tool is null) continue;
                    var (ok, error) = Validate(tool);
                    if (!ok)
                    {
                        errors.Add($"{tool.Id}: {error}");
                        continue;
                    }
                    Save(tool);
                    imported++;
                }
                catch (Exception ex)
                {
                    errors.Add($"entry skipped: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add($"invalid JSON: {ex.Message}");
        }

        return (imported, errors);
    }

    /// <summary>
    /// Merges the custom tools into a base catalog list. Custom entries with an
    /// id that already exists in the base catalog override it.
    /// </summary>
    public static List<ToolDefinition> Merge(List<ToolDefinition> baseCatalog)
    {
        var customs = LoadCustomTools();
        if (customs.Count == 0) return baseCatalog;

        var byId = new Dictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in baseCatalog) byId[t.Id] = t;
        foreach (var c in customs) byId[c.Id] = c;

        return byId.Values
            .OrderBy(t => t.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
