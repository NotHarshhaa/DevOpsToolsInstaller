using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using DevOpsToolsInstaller.Models;

namespace DevOpsToolsInstaller.Services;

/// <summary>
/// A user-authored pre/post-install hook for a specific tool. Hooks are shell
/// commands the user registers themselves — the app never invents them and
/// only runs them when hooks are explicitly enabled in Settings.
/// </summary>
public sealed class ToolHook
{
    public string ToolId { get; set; } = string.Empty;

    /// <summary>Command run before the artifact action (install/extract/copy).</summary>
    public string PreInstallCommand { get; set; } = string.Empty;

    /// <summary>Command run after a successful artifact action.</summary>
    public string PostInstallCommand { get; set; } = string.Empty;

    /// <summary>Per-hook kill switch (in addition to the global HooksEnabled setting).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Maximum run time for each command, in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 120;

    public bool HasPre => !string.IsNullOrWhiteSpace(PreInstallCommand);
    public bool HasPost => !string.IsNullOrWhiteSpace(PostInstallCommand);
}

/// <summary>Outcome of a hook execution attempt.</summary>
public sealed record HookRunResult(bool Ran, bool Success, string Message)
{
    public static HookRunResult Skipped(string reason) => new(false, true, reason);
}

/// <summary>
/// Manages <b>user-defined</b> pre/post-install hooks (roadmap: "Per-tool
/// pre/post-install scripts — user-reviewed, never silent").
///
/// Design principles that match the rest of the app:
///   • Hooks are <b>opt-in</b>: nothing runs unless <see cref="SettingsService.HooksEnabled"/>
///     is on AND the individual hook is enabled.
///   • Every command is written verbatim to the activity/audit log before it
///     runs, so there is always a reviewable trail.
///   • Hooks are plain shell commands authored by the user — the app never
///     downloads or generates them, and never escalates privileges.
///   • Failures never silently abort an install; they are logged and surfaced.
///
/// Commands support these placeholders (expanded at run time):
///   {tool} {toolId} {version} {file} {downloadsFolder} {toolsBin} {toolsRoot}
/// </summary>
public static class HookService
{
    private static readonly string StorageFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DevOpsToolsInstaller", "hooks.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static readonly object Gate = new();

    /// <summary>Folder that stores the hooks definition file.</summary>
    public static string StorageFolder => Path.GetDirectoryName(StorageFilePath) ?? string.Empty;

    // ── Persistence ──────────────────────────────────────────────────────

    public static List<ToolHook> LoadAll()
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(StorageFilePath)) return new List<ToolHook>();
                var json = File.ReadAllText(StorageFilePath);
                return JsonSerializer.Deserialize<List<ToolHook>>(json, JsonOptions) ?? new List<ToolHook>();
            }
            catch
            {
                return new List<ToolHook>();
            }
        }
    }

    private static void SaveAll(List<ToolHook> hooks)
    {
        lock (Gate)
        {
            try
            {
                var dir = Path.GetDirectoryName(StorageFilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(StorageFilePath, JsonSerializer.Serialize(hooks, JsonOptions));
            }
            catch
            {
                // Best-effort persistence.
            }
        }
    }

    public static ToolHook? Get(string toolId)
    {
        if (string.IsNullOrWhiteSpace(toolId)) return null;
        return LoadAll().FirstOrDefault(h =>
            string.Equals(h.ToolId, toolId, StringComparison.OrdinalIgnoreCase));
    }

    public static void Save(ToolHook hook)
    {
        if (hook is null || string.IsNullOrWhiteSpace(hook.ToolId)) return;

        var all = LoadAll();
        var idx = all.FindIndex(h => string.Equals(h.ToolId, hook.ToolId, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0) all[idx] = hook; else all.Add(hook);
        SaveAll(all);
    }

    public static void Delete(string toolId)
    {
        var all = LoadAll();
        all.RemoveAll(h => string.Equals(h.ToolId, toolId, StringComparison.OrdinalIgnoreCase));
        SaveAll(all);
    }

    /// <summary>True when the tool has at least one enabled hook and hooks are globally on.</summary>
    public static bool HasHooks(string toolId)
    {
        var hook = Get(toolId);
        return hook is { Enabled: true } && (hook.HasPre || hook.HasPost);
    }

    // ── Execution ────────────────────────────────────────────────────────

    /// <summary>Runs the pre-install hook for a tool (if any). Never throws.</summary>
    public static HookRunResult RunPreInstall(ToolDefinition tool)
        => RunCommand(tool, isPre: true, Get(tool.Id)?.PreInstallCommand, Get(tool.Id)?.TimeoutSeconds ?? 120, Get(tool.Id)?.Enabled ?? false);

    /// <summary>Runs the post-install hook for a tool (if any). Never throws.</summary>
    public static HookRunResult RunPostInstall(ToolDefinition tool)
        => RunCommand(tool, isPre: false, Get(tool.Id)?.PostInstallCommand, Get(tool.Id)?.TimeoutSeconds ?? 120, Get(tool.Id)?.Enabled ?? false);

    /// <summary>Runs an arbitrary command string for a tool (used by the UI "Test" button).</summary>
    public static HookRunResult RunAdHoc(ToolDefinition tool, string command, int timeoutSeconds = 120, bool isPre = true)
        => RunCommand(tool, isPre, command, timeoutSeconds, enabled: true, force: true);

    private static HookRunResult RunCommand(
        ToolDefinition tool, bool isPre, string? command, int timeoutSeconds, bool enabled, bool force = false)
    {
        var label = isPre ? "pre-install" : "post-install";

        if (string.IsNullOrWhiteSpace(command))
            return HookRunResult.Skipped($"{tool.Name}: no {label} hook configured.");

        if (!enabled && !force)
            return HookRunResult.Skipped($"{tool.Name}: {label} hook disabled.");

        if (!SettingsService.HooksEnabled && !force)
            return HookRunResult.Skipped($"{tool.Name}: hooks are disabled in Settings.");

        var expanded = ExpandPlaceholders(command!, tool);
        var timeout = Math.Clamp(timeoutSeconds <= 0 ? 120 : timeoutSeconds, 5, 3600);

        ActivityLogService.Info(tool.Name, $"Running {label} hook: {expanded}");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/d /s /c " + expanded,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = ArtifactService.BinFolder
            };

            using var proc = Process.Start(psi);
            if (proc is null)
                return new HookRunResult(true, false, $"{tool.Name}: could not start the {label} hook.");

            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();

            if (!proc.WaitForExit(timeout * 1000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                ActivityLogService.Warn(tool.Name, $"{label} hook timed out after {timeout}s.");
                return new HookRunResult(true, false, $"{tool.Name}: {label} hook timed out after {timeout}s.");
            }

            var combined = Truncate(string.Join(Environment.NewLine,
                new[] { stdout, stderr }.Where(s => !string.IsNullOrWhiteSpace(s))), 4000);

            if (proc.ExitCode == 0)
            {
                ActivityLogService.Success(tool.Name, $"{label} hook completed (exit 0).");
                return new HookRunResult(true, true, $"{tool.Name}: {label} hook completed.");
            }

            ActivityLogService.Warn(tool.Name, $"{label} hook exit code {proc.ExitCode}. {combined}");
            return new HookRunResult(true, false,
                $"{tool.Name}: {label} hook exited with code {proc.ExitCode}.");
        }
        catch (Exception ex)
        {
            ActivityLogService.Error(tool.Name, $"{label} hook failed: {ex.Message}");
            return new HookRunResult(true, false, $"{tool.Name}: {label} hook failed — {ex.Message}");
        }
    }

    /// <summary>Expands the supported placeholders in a hook command.</summary>
    public static string ExpandPlaceholders(string command, ToolDefinition tool)
    {
        var filePath = Path.Combine(SettingsService.DownloadsFolder, tool.FileName);
        return command
            .Replace("{tool}", tool.Name, StringComparison.OrdinalIgnoreCase)
            .Replace("{toolId}", tool.Id, StringComparison.OrdinalIgnoreCase)
            .Replace("{version}", tool.SelectedVersion, StringComparison.OrdinalIgnoreCase)
            .Replace("{file}", filePath, StringComparison.OrdinalIgnoreCase)
            .Replace("{downloadsFolder}", SettingsService.DownloadsFolder, StringComparison.OrdinalIgnoreCase)
            .Replace("{toolsBin}", ArtifactService.BinFolder, StringComparison.OrdinalIgnoreCase)
            .Replace("{toolsRoot}", ArtifactService.ToolsRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max] + "…";
}
