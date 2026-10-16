using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevOpsToolsInstaller.Models;

namespace DevOpsToolsInstaller.Services;

/// <summary>One tool captured inside a snapshot.</summary>
public sealed class SnapshotEntry
{
    public string ToolId { get; set; } = string.Empty;
    public string ToolName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
}

/// <summary>A point-in-time capture of the workstation toolset.</summary>
public sealed class WorkstationSnapshot
{
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string MachineName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string UserPath { get; set; } = string.Empty;
    public List<SnapshotEntry> Entries { get; set; } = new();

    [JsonIgnore] public string CreatedLabel => CreatedAt.ToString("yyyy-MM-dd HH:mm");
    [JsonIgnore] public int ToolCount => Entries.Count;
    [JsonIgnore] public string Subtitle => $"{ToolCount} tool(s) · {CreatedLabel} · {MachineName}";
}

/// <summary>Result of diffing a snapshot against the current workstation.</summary>
public sealed record SnapshotDiff(
    List<SnapshotEntry> Missing,
    List<SnapshotEntry> Extra,
    List<(SnapshotEntry Snapshot, SnapshotEntry Current)> Changed)
{
    public bool IsEmpty => Missing.Count == 0 && Extra.Count == 0 && Changed.Count == 0;
}

/// <summary>
/// Workstation snapshots / restore points (roadmap: "System snapshot &amp;
/// restore point"). A snapshot records the installed tools, their versions, and
/// the User PATH at a moment in time. Later you can diff it against the live
/// workstation and restore (reinstall) anything that went missing.
/// </summary>
public static class SnapshotService
{
    private static readonly string SnapshotsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DevOpsToolsInstaller", "snapshots");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string StorageFolder => SnapshotsFolder;

    public static List<WorkstationSnapshot> LoadAll()
    {
        var results = new List<WorkstationSnapshot>();
        try
        {
            if (!Directory.Exists(SnapshotsFolder)) return results;
            foreach (var file in Directory.GetFiles(SnapshotsFolder, "*.json"))
            {
                try
                {
                    var snap = JsonSerializer.Deserialize<WorkstationSnapshot>(File.ReadAllText(file), JsonOptions);
                    if (snap is not null)
                    {
                        if (string.IsNullOrWhiteSpace(snap.Name))
                            snap.Name = Path.GetFileNameWithoutExtension(file);
                        results.Add(snap);
                    }
                }
                catch
                {
                    // Skip a corrupt snapshot rather than failing the whole list.
                }
            }
        }
        catch
        {
            // Best effort.
        }

        return results.OrderByDescending(s => s.CreatedAt).ToList();
    }

    public static WorkstationSnapshot? Get(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return LoadAll().FirstOrDefault(s =>
            string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static string PathFor(string name)
        => Path.Combine(SnapshotsFolder, Sanitize(name) + ".json");

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string((name ?? "snapshot").Trim()
            .Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "snapshot" : clean;
    }

    /// <summary>
    /// Captures the currently installed tools into a named snapshot. Overwrites
    /// an existing snapshot with the same name.
    /// </summary>
    public static WorkstationSnapshot Capture(string name, IReadOnlyList<ToolDefinition> catalogTools)
    {
        var dlFolder = DownloadService.DefaultDownloadsFolder;
        var entries = new List<SnapshotEntry>();

        foreach (var tool in catalogTools)
        {
            if (!UninstallService.IsInstalled(tool, dlFolder) && tool.Status != ToolStatus.Downloaded)
                continue;

            var version = tool.Kind == ArtifactKind.Installer
                ? (UninstallService.GetInstalledVersion(tool) ?? tool.DetectedVersion ?? string.Empty)
                : (tool.DetectedVersion ?? string.Empty);

            entries.Add(new SnapshotEntry
            {
                ToolId = tool.Id,
                ToolName = tool.Name,
                Version = version,
                Kind = tool.Kind.ToString()
            });
        }

        var snapshot = new WorkstationSnapshot
        {
            Name = string.IsNullOrWhiteSpace(name) ? $"snapshot-{DateTime.Now:yyyyMMdd-HHmm}" : name.Trim(),
            CreatedAt = DateTime.Now,
            MachineName = Environment.MachineName,
            UserName = Environment.UserName,
            UserPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? string.Empty,
            Entries = entries.OrderBy(e => e.ToolName, StringComparer.OrdinalIgnoreCase).ToList()
        };

        Save(snapshot);
        InstallHistoryService.Record(HistoryAction.Custom, "-", "Snapshot",
            string.Empty, $"Captured snapshot '{snapshot.Name}' ({entries.Count} tools).");
        return snapshot;
    }

    public static void Save(WorkstationSnapshot snapshot)
    {
        try
        {
            Directory.CreateDirectory(SnapshotsFolder);
            File.WriteAllText(PathFor(snapshot.Name), JsonSerializer.Serialize(snapshot, JsonOptions));
        }
        catch
        {
            // Best effort.
        }
    }

    public static void Delete(string name)
    {
        try
        {
            var path = PathFor(name);
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best effort.
        }
    }

    /// <summary>Diffs a snapshot against the live workstation state.</summary>
    public static SnapshotDiff Diff(WorkstationSnapshot snapshot, IReadOnlyList<ToolDefinition> catalogTools)
    {
        var dlFolder = DownloadService.DefaultDownloadsFolder;
        var current = new Dictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase);

        foreach (var tool in catalogTools)
        {
            if (UninstallService.IsInstalled(tool, dlFolder) || tool.Status == ToolStatus.Downloaded)
                current[tool.Id] = tool;
        }

        var missing = new List<SnapshotEntry>();
        var extra = new List<SnapshotEntry>();
        var changed = new List<(SnapshotEntry, SnapshotEntry)>();

        var snapshotIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in snapshot.Entries)
        {
            snapshotIds.Add(entry.ToolId);
            if (!current.TryGetValue(entry.ToolId, out var tool))
            {
                missing.Add(entry);
                continue;
            }

            var currentVersion = tool.Kind == ArtifactKind.Installer
                ? (UninstallService.GetInstalledVersion(tool) ?? tool.DetectedVersion ?? string.Empty)
                : (tool.DetectedVersion ?? string.Empty);

            if (!string.IsNullOrWhiteSpace(entry.Version) &&
                !string.IsNullOrWhiteSpace(currentVersion) &&
                !string.Equals(Normalize(entry.Version), Normalize(currentVersion), StringComparison.OrdinalIgnoreCase))
            {
                changed.Add((entry, new SnapshotEntry
                {
                    ToolId = tool.Id,
                    ToolName = tool.Name,
                    Version = currentVersion,
                    Kind = tool.Kind.ToString()
                }));
            }
        }

        foreach (var tool in current.Values)
        {
            if (!snapshotIds.Contains(tool.Id))
            {
                extra.Add(new SnapshotEntry
                {
                    ToolId = tool.Id,
                    ToolName = tool.Name,
                    Version = tool.DetectedVersion ?? string.Empty,
                    Kind = tool.Kind.ToString()
                });
            }
        }

        return new SnapshotDiff(missing, extra, changed);
    }

    /// <summary>Resolves the catalog tools that a snapshot references but are not installed.</summary>
    public static List<ToolDefinition> GetMissingTools(WorkstationSnapshot snapshot, IReadOnlyList<ToolDefinition> catalogTools)
    {
        var dlFolder = DownloadService.DefaultDownloadsFolder;
        var lookup = catalogTools.ToDictionary(t => t.Id, t => t, StringComparer.OrdinalIgnoreCase);
        var missing = new List<ToolDefinition>();

        foreach (var entry in snapshot.Entries)
        {
            if (!lookup.TryGetValue(entry.ToolId, out var tool)) continue;
            if (UninstallService.IsInstalled(tool, dlFolder) || tool.Status == ToolStatus.Downloaded) continue;
            missing.Add(tool);
        }

        return missing;
    }

    private static string Normalize(string version)
        => version.Trim().TrimStart('v', 'V');
}
