using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace DevOpsToolsInstaller.Services;

public enum PathIssueSeverity
{
    Information,
    Warning,
    Error
}

public sealed record PathDiagnosticItem(
    string Entry,
    string IssueType,
    string Message,
    PathIssueSeverity Severity);

public sealed record PathDoctorReport(
    bool IsHealthy,
    int TotalEntries,
    IReadOnlyList<string> OriginalEntries,
    IReadOnlyList<string> CleanedEntries,
    IReadOnlyList<string> DuplicateEntries,
    IReadOnlyList<string> MissingDirectories,
    int EmptyEntriesCount,
    bool IsBinOnUserPath,
    IReadOnlyList<PathDiagnosticItem> Diagnostics,
    string Summary);

public sealed record PathFixResult(
    bool Success,
    string BackupFilePath,
    int RemovedDuplicatesCount,
    int RemovedMissingCount,
    bool AddedBinFolder,
    string Message);

public sealed record PathBackupInfo(
    string FilePath,
    string FileName,
    DateTime Timestamp,
    int EntryCount,
    string ContentPreview);

/// <summary>
/// Description of the safe, non-destructive PATH cleanups offered by the
/// "Fix all safe issues" action: duplicate entries and directories that no
/// longer exist. Produced by <see cref="PathDoctorService.PlanSafeFixes"/> and
/// applied by <see cref="PathDoctorService.ApplySafeFixes"/>.
/// </summary>
public sealed record PathSafeFixPlan(
    IReadOnlyList<string> DuplicateEntries,
    IReadOnlyList<string> MissingDirectories)
{
    public int TotalChanges => DuplicateEntries.Count + MissingDirectories.Count;
    public bool HasChanges => TotalChanges > 0;
}

public static class PathDoctorService
{
    private static readonly string BackupDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DevOpsToolsInstaller", "Backups", "PATH");

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint Msg,
        UIntPtr wParam,
        string lParam,
        uint fuFlags,
        uint uTimeout,
        out UIntPtr lpdwResult);

    private const int HWND_BROADCAST = 0xFFFF;
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    public static PathDoctorReport Diagnose()
    {
        var binFolder = ArtifactService.BinFolder.TrimEnd('\\', '/');
        var rawUserPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? string.Empty;

        var rawParts = rawUserPath.Split(';');
        var originalEntries = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new List<string>();
        var missing = new List<string>();
        var diagnostics = new List<PathDiagnosticItem>();
        int emptyCount = 0;

        foreach (var raw in rawParts)
        {
            var trimmed = raw.Trim().Trim('\"');
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                emptyCount++;
                continue;
            }

            originalEntries.Add(trimmed);

            // Duplicate check
            var normalized = trimmed.TrimEnd('\\', '/');
            if (!seen.Add(normalized))
            {
                duplicates.Add(trimmed);
                diagnostics.Add(new PathDiagnosticItem(
                    trimmed,
                    "Duplicate Entry",
                    "Path is defined multiple times in User PATH, causing redundant lookups.",
                    PathIssueSeverity.Warning));
            }

            // Directory existence check (expanding environment variables like %USERPROFILE%)
            var expanded = Environment.ExpandEnvironmentVariables(trimmed);
            if (!Directory.Exists(expanded))
            {
                missing.Add(trimmed);
                diagnostics.Add(new PathDiagnosticItem(
                    trimmed,
                    "Missing Directory",
                    "Directory does not exist on disk, slowing down CLI binary resolution.",
                    PathIssueSeverity.Error));
            }
        }

        bool isBinOnUserPath = seen.Contains(binFolder);
        if (!isBinOnUserPath)
        {
            diagnostics.Add(new PathDiagnosticItem(
                binFolder,
                "Tools\\bin Not Configured",
                "The DevOpsToolsInstaller tools directory is not on User PATH. Extracted CLI tools cannot be launched directly from terminal.",
                PathIssueSeverity.Warning));
        }

        // Build cleaned list
        var cleanedList = new List<string>();
        var cleanedSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Always put Tools\bin at the front or preserved position
        if (Directory.Exists(binFolder))
        {
            cleanedList.Add(binFolder);
            cleanedSeen.Add(binFolder);
        }

        foreach (var entry in originalEntries)
        {
            var normalized = entry.TrimEnd('\\', '/');
            var expanded = Environment.ExpandEnvironmentVariables(entry);

            if (cleanedSeen.Contains(normalized)) continue;
            if (!Directory.Exists(expanded)) continue;

            cleanedList.Add(entry);
            cleanedSeen.Add(normalized);
        }

        bool isHealthy = duplicates.Count == 0 && missing.Count == 0 && emptyCount == 0 && isBinOnUserPath;

        string summary;
        if (isHealthy)
        {
            summary = "User PATH is clean, healthy, and properly configured with Tools\\bin.";
        }
        else
        {
            var issues = new List<string>();
            if (duplicates.Count > 0) issues.Add($"{duplicates.Count} duplicate(s)");
            if (missing.Count > 0) issues.Add($"{missing.Count} broken path(s)");
            if (emptyCount > 0) issues.Add($"{emptyCount} empty segment(s)");
            if (!isBinOnUserPath) issues.Add("Tools\\bin missing");
            summary = $"Found {string.Join(", ", issues)} in User PATH.";
        }

        return new PathDoctorReport(
            IsHealthy: isHealthy,
            TotalEntries: originalEntries.Count,
            OriginalEntries: originalEntries,
            CleanedEntries: cleanedList,
            DuplicateEntries: duplicates,
            MissingDirectories: missing,
            EmptyEntriesCount: emptyCount,
            IsBinOnUserPath: isBinOnUserPath,
            Diagnostics: diagnostics,
            Summary: summary);
    }

    public static PathFixResult FixUserPath(bool removeDuplicates = true, bool removeMissing = true, bool ensureBinFolder = true)
    {
        try
        {
            var rawUserPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? string.Empty;

            // 1. Create timestamped backup
            Directory.CreateDirectory(BackupDirectory);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupFile = Path.Combine(BackupDirectory, $"path_user_backup_{timestamp}.txt");
            File.WriteAllText(backupFile, rawUserPath);

            var report = Diagnose();
            var binFolder = ArtifactService.BinFolder.TrimEnd('\\', '/');

            var finalEntries = new List<string>();
            var finalSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (ensureBinFolder)
            {
                finalEntries.Add(binFolder);
                finalSeen.Add(binFolder);
            }

            int removedDuplicates = 0;
            int removedMissing = 0;

            foreach (var entry in report.OriginalEntries)
            {
                var normalized = entry.TrimEnd('\\', '/');
                var expanded = Environment.ExpandEnvironmentVariables(entry);

                if (removeDuplicates && finalSeen.Contains(normalized))
                {
                    removedDuplicates++;
                    continue;
                }

                if (removeMissing && !Directory.Exists(expanded))
                {
                    removedMissing++;
                    continue;
                }

                finalEntries.Add(entry);
                finalSeen.Add(normalized);
            }

            var newPathValue = string.Join(";", finalEntries);
            Environment.SetEnvironmentVariable("PATH", newPathValue, EnvironmentVariableTarget.User);

            // Broadcast setting change
            BroadcastEnvironmentChange();

            return new PathFixResult(
                Success: true,
                BackupFilePath: backupFile,
                RemovedDuplicatesCount: removedDuplicates,
                RemovedMissingCount: removedMissing,
                AddedBinFolder: ensureBinFolder && !report.IsBinOnUserPath,
                Message: $"Successfully optimized User PATH. Backup saved to {Path.GetFileName(backupFile)}.");
        }
        catch (Exception ex)
        {
            return new PathFixResult(
                Success: false,
                BackupFilePath: string.Empty,
                RemovedDuplicatesCount: 0,
                RemovedMissingCount: 0,
                AddedBinFolder: false,
                Message: $"Failed to optimize User PATH: {ex.Message}");
        }
    }

    /// <summary>
    /// Prepares the safe PATH cleanups (duplicate and dead entries) so the UI can
    /// show exactly what "Fix all safe issues" will change before it runs.
    /// </summary>
    public static PathSafeFixPlan PlanSafeFixes()
    {
        var report = Diagnose();

        // An entry that is both duplicated and dead is listed once, under
        // duplicates, so the plan never counts or shows the same path twice.
        var duplicateKeys = report.DuplicateEntries
            .Select(e => e.Trim().TrimEnd('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = report.MissingDirectories
            .Where(m => !duplicateKeys.Contains(m.Trim().TrimEnd('\\', '/')))
            .ToList();

        return new PathSafeFixPlan(report.DuplicateEntries, missing);
    }

    /// <summary>
    /// Removes duplicate and non-existent entries from the User PATH. The original
    /// value is backed up first (its path is returned in
    /// <see cref="PathFixResult.BackupFilePath"/>) so the change can be undone with
    /// <see cref="RestoreBackup"/>.
    /// </summary>
    public static PathFixResult ApplySafeFixes()
        => FixUserPath(removeDuplicates: true, removeMissing: true, ensureBinFolder: false);

    public static IReadOnlyList<PathBackupInfo> GetBackups()
    {
        var list = new List<PathBackupInfo>();
        if (!Directory.Exists(BackupDirectory)) return list;

        foreach (var file in Directory.GetFiles(BackupDirectory, "path_user_backup_*.txt")
                                      .OrderByDescending(f => File.GetCreationTime(f)))
        {
            try
            {
                var content = File.ReadAllText(file);
                var entries = content.Split(';', StringSplitOptions.RemoveEmptyEntries);
                var preview = entries.Length > 0
                    ? string.Join("; ", entries.Take(3)) + (entries.Length > 3 ? "..." : "")
                    : "(Empty PATH)";

                list.Add(new PathBackupInfo(
                    FilePath: file,
                    FileName: Path.GetFileName(file),
                    Timestamp: File.GetCreationTime(file),
                    EntryCount: entries.Length,
                    ContentPreview: preview));
            }
            catch { }
        }

        return list;
    }

    public static bool RestoreBackup(string backupFilePath)
    {
        if (!File.Exists(backupFilePath)) return false;

        try
        {
            var content = File.ReadAllText(backupFilePath);
            Environment.SetEnvironmentVariable("PATH", content, EnvironmentVariableTarget.User);
            BroadcastEnvironmentChange();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void BroadcastEnvironmentChange()
    {
        try
        {
            SendMessageTimeout(
                (IntPtr)HWND_BROADCAST,
                WM_SETTINGCHANGE,
                UIntPtr.Zero,
                "Environment",
                SMTO_ABORTIFHUNG,
                1000,
                out _);
        }
        catch { }
    }
}
