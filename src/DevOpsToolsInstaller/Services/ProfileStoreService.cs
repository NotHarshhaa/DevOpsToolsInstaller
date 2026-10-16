using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevOpsToolsInstaller.Models;

namespace DevOpsToolsInstaller.Services;

/// <summary>
/// A named, reusable workstation profile: a labelled set of catalog tool ids
/// that can be applied (selected) or installed on any machine.
/// </summary>
public sealed class WorkstationProfile
{
    public string Id { get; set; } = "profile-" + Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "New profile";
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    /// <summary>"captured" (from the installed set) or "custom" (hand-picked).</summary>
    public string Source { get; set; } = "custom";

    public List<string> ToolIds { get; set; } = new();

    [JsonIgnore] public int ToolCount => ToolIds.Count;
    [JsonIgnore] public string CreatedLabel => UpdatedAt.ToString("yyyy-MM-dd HH:mm");
    [JsonIgnore]
    public string SourceLabel => Source.Equals("captured", StringComparison.OrdinalIgnoreCase)
        ? "Captured" : "Custom";

    [JsonIgnore]
    public string Subtitle => string.IsNullOrWhiteSpace(Description)
        ? $"{ToolCount} tool(s) · {SourceLabel} · updated {CreatedLabel}"
        : $"{Description} · {ToolCount} tool(s)";
}

/// <summary>
/// Storage for named workstation profiles (roadmap: "Named workstation
/// profiles manager"). Profiles live in
/// %LOCALAPPDATA%\DevOpsToolsInstaller\profiles.json — separate from the
/// one-shot tool-selection export handled by <see cref="ProfileService"/>.
/// </summary>
public static class ProfileStoreService
{
    private static readonly string StorageFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DevOpsToolsInstaller", "profiles.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string FilePath => StorageFilePath;

    private static readonly object Gate = new();

    public static List<WorkstationProfile> LoadAll()
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(StorageFilePath)) return new List<WorkstationProfile>();
                var json = File.ReadAllText(StorageFilePath);
                return JsonSerializer.Deserialize<List<WorkstationProfile>>(json, JsonOptions)
                       ?? new List<WorkstationProfile>();
            }
            catch
            {
                return new List<WorkstationProfile>();
            }
        }
    }

    public static WorkstationProfile? Get(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return LoadAll().FirstOrDefault(p =>
            string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.Name, id, StringComparison.OrdinalIgnoreCase));
    }

    public static void Save(WorkstationProfile profile)
    {
        if (profile is null || string.IsNullOrWhiteSpace(profile.Name)) return;

        lock (Gate)
        {
            try
            {
                var all = LoadAll();
                profile.UpdatedAt = DateTime.Now;
                var idx = all.FindIndex(p => string.Equals(p.Id, profile.Id, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) all[idx] = profile; else all.Add(profile);

                var dir = Path.GetDirectoryName(StorageFilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(StorageFilePath, JsonSerializer.Serialize(all, JsonOptions));
            }
            catch
            {
                // Best-effort persistence.
            }
        }
    }

    public static void Delete(string id)
    {
        lock (Gate)
        {
            try
            {
                var all = LoadAll();
                all.RemoveAll(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
                var dir = Path.GetDirectoryName(StorageFilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(StorageFilePath, JsonSerializer.Serialize(all, JsonOptions));
            }
            catch { }
        }
    }

    /// <summary>Replaces the on-disk profile list (used by sync/import).</summary>
    public static void ReplaceAll(List<WorkstationProfile> profiles)
    {
        lock (Gate)
        {
            try
            {
                var dir = Path.GetDirectoryName(StorageFilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(StorageFilePath, JsonSerializer.Serialize(profiles, JsonOptions));
            }
            catch { }
        }
    }

    /// <summary>Creates a profile from the set of tools currently installed.</summary>
    public static WorkstationProfile CaptureFromInstalled(
        string name, string description, IReadOnlyList<ToolDefinition> catalogTools)
    {
        var dlFolder = DownloadService.DefaultDownloadsFolder;
        var ids = catalogTools
            .Where(t => UninstallService.IsInstalled(t, dlFolder) || t.Status == ToolStatus.Downloaded)
            .Select(t => t.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var profile = new WorkstationProfile
        {
            Name = string.IsNullOrWhiteSpace(name) ? $"{Environment.MachineName} — {DateTime.Now:yyyy-MM-dd}" : name.Trim(),
            Description = description ?? string.Empty,
            Source = "captured",
            ToolIds = ids
        };

        Save(profile);
        return profile;
    }

    /// <summary>Creates a profile from a set of selected/hand-picked tool ids.</summary>
    public static WorkstationProfile CreateFromSelection(
        string name, string description, IEnumerable<string> toolIds)
    {
        var profile = new WorkstationProfile
        {
            Name = string.IsNullOrWhiteSpace(name) ? "New profile" : name.Trim(),
            Description = description ?? string.Empty,
            Source = "custom",
            ToolIds = toolIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };

        Save(profile);
        return profile;
    }

    /// <summary>
    /// Applies a profile to the catalog by setting <see cref="ToolDefinition.IsSelected"/>
    /// on its member tools. Returns the number of tools selected.
    /// </summary>
    public static int Apply(WorkstationProfile profile, IEnumerable<ToolDefinition> catalogTools)
    {
        var targets = new HashSet<string>(profile.ToolIds, StringComparer.OrdinalIgnoreCase);
        int count = 0;

        foreach (var tool in catalogTools)
        {
            tool.IsSelected = targets.Contains(tool.Id);
            if (tool.IsSelected) count++;
        }

        return count;
    }

    /// <summary>Returns the catalog tools referenced by a profile (unknown ids skipped).</summary>
    public static List<ToolDefinition> ResolveTools(
        WorkstationProfile profile, IReadOnlyList<ToolDefinition> catalogTools)
    {
        var lookup = catalogTools.ToDictionary(t => t.Id, t => t, StringComparer.OrdinalIgnoreCase);
        var result = new List<ToolDefinition>();

        foreach (var id in profile.ToolIds)
        {
            if (lookup.TryGetValue(id, out var tool))
                result.Add(tool);
        }

        return result;
    }

    public static string ExportJson(WorkstationProfile profile)
        => JsonSerializer.Serialize(profile, JsonOptions);

    /// <summary>Imports a single profile from JSON, returning it (unsaved) or an error.</summary>
    public static (WorkstationProfile? Profile, string? Error) ImportJson(string json)
    {
        try
        {
            var profile = JsonSerializer.Deserialize<WorkstationProfile>(json, JsonOptions);
            if (profile is null) return (null, "The file did not contain a profile.");
            if (profile.ToolIds.Count == 0) return (null, "The profile does not reference any tools.");
            profile.Id = "profile-" + Guid.NewGuid().ToString("N")[..8];
            return (profile, null);
        }
        catch (Exception ex)
        {
            return (null, $"Invalid profile JSON: {ex.Message}");
        }
    }
}
