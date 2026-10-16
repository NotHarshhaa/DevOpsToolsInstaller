using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DevOpsToolsInstaller.Services;

/// <summary>Kind of change recorded in the workstation install history.</summary>
public enum HistoryAction
{
    Install,
    Update,
    Uninstall,
    Rollback,
    Custom
}

/// <summary>A single append-only workstation event.</summary>
public sealed class InstallHistoryEvent
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string ToolId { get; set; } = string.Empty;
    public string ToolName { get; set; } = string.Empty;
    public string Action { get; set; } = HistoryAction.Custom.ToString();
    public string Version { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;

    // Display helpers (bound by the UI).
    public string TimeLabel => Timestamp.ToString("yyyy-MM-dd HH:mm");
    public string ActionLabel => Action;
    public string Summary => string.IsNullOrWhiteSpace(Version)
        ? Action
        : $"{Action} v{Version.TrimStart('v', 'V')}";

    public string ActionGlyph => Action switch
    {
        nameof(HistoryAction.Install) => "\uE896",
        nameof(HistoryAction.Update) => "\uE895",
        nameof(HistoryAction.Uninstall) => "\uE74D",
        nameof(HistoryAction.Rollback) => "\uE777",
        _ => "\uE946"
    };
}

/// <summary>
/// Append-only install/update/uninstall history (roadmap: "Install history &amp;
/// rollback"). Events are stored as JSON Lines in
/// %LOCALAPPDATA%\DevOpsToolsInstaller\history.jsonl so the file stays cheap to
/// append and easy to inspect. The per-tool version history powers rollback.
/// </summary>
public static class InstallHistoryService
{
    private static readonly string StorageFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DevOpsToolsInstaller", "history.jsonl");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly object Gate = new();

    public static void Record(HistoryAction action, string toolId, string toolName, string version, string detail)
    {
        var evt = new InstallHistoryEvent
        {
            Timestamp = DateTime.Now,
            ToolId = toolId ?? string.Empty,
            ToolName = toolName ?? string.Empty,
            Action = action.ToString(),
            Version = version ?? string.Empty,
            Detail = detail ?? string.Empty
        };

        lock (Gate)
        {
            try
            {
                var dir = Path.GetDirectoryName(StorageFilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(StorageFilePath, JsonSerializer.Serialize(evt, JsonOptions) + Environment.NewLine);
            }
            catch
            {
                // History is best-effort; never fail an install because of it.
            }
        }
    }

    /// <summary>Convenience overloads.</summary>
    public static void RecordInstall(string toolId, string toolName, string version, string detail = "")
        => Record(HistoryAction.Install, toolId, toolName, version, detail);

    public static void RecordUpdate(string toolId, string toolName, string version, string detail = "")
        => Record(HistoryAction.Update, toolId, toolName, version, detail);

    public static void RecordUninstall(string toolId, string toolName, string version, string detail = "")
        => Record(HistoryAction.Uninstall, toolId, toolName, version, detail);

    public static void RecordRollback(string toolId, string toolName, string version, string detail = "")
        => Record(HistoryAction.Rollback, toolId, toolName, version, detail);

    /// <summary>Loads the most recent events (newest first).</summary>
    public static List<InstallHistoryEvent> Load(int max = 500)
    {
        lock (Gate)
        {
            var results = new List<InstallHistoryEvent>();
            try
            {
                if (!File.Exists(StorageFilePath)) return results;

                foreach (var line in File.ReadLines(StorageFilePath))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var evt = JsonSerializer.Deserialize<InstallHistoryEvent>(line, JsonOptions);
                        if (evt is not null) results.Add(evt);
                    }
                    catch
                    {
                        // Skip malformed lines rather than failing the whole read.
                    }
                }
            }
            catch
            {
                return results;
            }

            return results
                .OrderByDescending(e => e.Timestamp)
                .Take(max)
                .ToList();
        }
    }

    public static List<InstallHistoryEvent> LoadForTool(string toolId, int max = 100)
    {
        if (string.IsNullOrWhiteSpace(toolId)) return new List<InstallHistoryEvent>();
        return Load(int.MaxValue)
            .Where(e => string.Equals(e.ToolId, toolId, StringComparison.OrdinalIgnoreCase))
            .Take(max)
            .ToList();
    }

    /// <summary>
    /// Distinct versions previously installed for a tool (newest first) — used
    /// to offer a rollback target.
    /// </summary>
    public static List<string> GetInstalledVersions(string toolId)
    {
        return LoadForTool(toolId)
            .Where(e => e.Action is nameof(HistoryAction.Install)
                        or nameof(HistoryAction.Update)
                        or nameof(HistoryAction.Rollback))
            .Select(e => e.Version)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static void Clear()
    {
        lock (Gate)
        {
            try { if (File.Exists(StorageFilePath)) File.Delete(StorageFilePath); }
            catch { }
        }
    }
}
