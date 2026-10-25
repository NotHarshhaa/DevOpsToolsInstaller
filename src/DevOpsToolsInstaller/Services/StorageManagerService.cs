using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DevOpsToolsInstaller.Services;

/// <summary>What a file in the downloads folder is, from the storage manager's point of view.</summary>
public enum StorageFileKind
{
    /// <summary>A completed, verified vendor artifact (.msi / .exe / .zip …).</summary>
    Artifact,
    /// <summary>An interrupted transfer kept for HTTP Range resume.</summary>
    Partial,
    /// <summary>The ETag sidecar belonging to a <c>.partial</c> file.</summary>
    PartialMeta
}

/// <summary>One file found by a storage scan.</summary>
public sealed record StorageFileItem(
    string FileName,
    StorageFileKind Kind,
    long Bytes,
    DateTime LastWriteUtc,
    bool IsProtected);

/// <summary>Aggregated disk-usage snapshot of the folders this app owns.</summary>
public sealed class StorageScanResult
{
    public string DownloadsFolder { get; init; } = string.Empty;
    public string ToolsFolder { get; init; } = string.Empty;
    public string LogsFolder { get; init; } = string.Empty;

    public IReadOnlyList<StorageFileItem> Files { get; init; } = Array.Empty<StorageFileItem>();

    public long ArtifactsBytes { get; init; }
    public long PartialBytes { get; init; }
    public long ToolsBytes { get; init; }
    public long LogsBytes { get; init; }

    public int ArtifactCount { get; init; }
    public int PartialCount { get; init; }
    public int ProtectedCount { get; init; }

    /// <summary>Age of the oldest completed artifact, if any.</summary>
    public DateTime? OldestArtifactUtc { get; init; }

    public long DownloadsBytes => ArtifactsBytes + PartialBytes;
    public long TotalBytes => DownloadsBytes + ToolsBytes + LogsBytes;

    /// <summary>Bytes that could be reclaimed right now (everything not mid-transfer).</summary>
    public long ReclaimableBytes => Files.Where(f => !f.IsProtected).Sum(f => f.Bytes);
}

/// <summary>Outcome of a cleanup run.</summary>
public sealed record StorageCleanResult(int DeletedFiles, long FreedBytes, int SkippedActive);

/// <summary>
/// Disk-usage analysis and safe cleanup for the folders DevOpsToolsInstaller
/// owns: the downloads cache (vendor artifacts + interrupted-transfer resume
/// state), the extracted Tools folder, and the local audit logs.
///
/// Every delete skips files that belong to a transfer currently in flight
/// (see <see cref="DownloadService.IsFileTransferActive"/>) and never touches
/// installed tools — removing a cached installer only means it would be
/// re-downloaded if ever needed again.
/// </summary>
public static class StorageManagerService
{
    /// <summary>Scans the app-owned folders and categorizes every file in the downloads cache.</summary>
    public static StorageScanResult Scan(string? downloadsFolder = null)
    {
        var dlFolder = downloadsFolder ?? SettingsService.DownloadsFolder;
        var toolsFolder = ArtifactService.ToolsRoot;
        var logsFolder = ActivityLogService.LogDirectory;

        var items = new List<StorageFileItem>();
        long artifacts = 0, partials = 0;
        int artifactCount = 0, partialCount = 0, protectedCount = 0;
        DateTime? oldest = null;

        if (Directory.Exists(dlFolder))
        {
            foreach (var path in Directory.EnumerateFiles(dlFolder))
            {
                try
                {
                    var info = new FileInfo(path);
                    var kind = Classify(info.Name);
                    var isProtected = DownloadService.IsFileTransferActive(path);

                    items.Add(new StorageFileItem(info.Name, kind, info.Length, info.LastWriteTimeUtc, isProtected));

                    if (isProtected) protectedCount++;

                    if (kind == StorageFileKind.Artifact)
                    {
                        artifacts += info.Length;
                        artifactCount++;
                        if (oldest is null || info.LastWriteTimeUtc < oldest) oldest = info.LastWriteTimeUtc;
                    }
                    else
                    {
                        partials += info.Length;
                        partialCount++;
                    }
                }
                catch
                {
                    // File vanished or is locked — ignore.
                }
            }
        }

        return new StorageScanResult
        {
            DownloadsFolder = dlFolder,
            ToolsFolder = toolsFolder,
            LogsFolder = logsFolder,
            Files = items,
            ArtifactsBytes = artifacts,
            PartialBytes = partials,
            ToolsBytes = GetFolderSize(toolsFolder),
            LogsBytes = GetFolderSize(logsFolder),
            ArtifactCount = artifactCount,
            PartialCount = partialCount,
            ProtectedCount = protectedCount,
            OldestArtifactUtc = oldest
        };
    }

    /// <summary>
    /// Deletes interrupted-transfer resume state (<c>.partial</c> files and their
    /// ETag sidecars). The next download of those tools starts from zero instead
    /// of resuming — nothing verified is lost.
    /// </summary>
    public static StorageCleanResult CleanResumeState(string? downloadsFolder = null)
    {
        var folder = downloadsFolder ?? SettingsService.DownloadsFolder;
        int deleted = 0, skipped = 0;
        long freed = 0;

        foreach (var path in EnumerateSafe(folder))
        {
            var kind = Classify(Path.GetFileName(path));
            if (kind != StorageFileKind.Partial && kind != StorageFileKind.PartialMeta) continue;
            if (DownloadService.IsFileTransferActive(path))
            {
                skipped++;
                continue;
            }

            if (TryDelete(path, out var bytes))
            {
                deleted++;
                freed += bytes;
            }
        }

        LogCleanup("interrupted transfer state", deleted, freed, skipped);
        return new StorageCleanResult(deleted, freed, skipped);
    }

    /// <summary>
    /// Deletes completed vendor artifacts from the downloads cache. Installed
    /// tools are unaffected; a deleted artifact is simply re-fetched if it is
    /// ever needed again. Pass <paramref name="olderThanDays"/> = 0 to delete
    /// every artifact, or N to keep fresh ones.
    /// </summary>
    public static StorageCleanResult CleanCompletedArtifacts(int olderThanDays = 0, string? downloadsFolder = null)
    {
        var folder = downloadsFolder ?? SettingsService.DownloadsFolder;
        var cutoffUtc = olderThanDays > 0
            ? DateTime.UtcNow - TimeSpan.FromDays(olderThanDays)
            : DateTime.MaxValue;

        int deleted = 0, skipped = 0;
        long freed = 0;

        foreach (var path in EnumerateSafe(folder))
        {
            if (Classify(Path.GetFileName(path)) != StorageFileKind.Artifact) continue;

            try
            {
                var info = new FileInfo(path);
                if (info.LastWriteTimeUtc > cutoffUtc) continue;

                if (DownloadService.IsFileTransferActive(path))
                {
                    skipped++;
                    continue;
                }

                if (TryDelete(path, out var bytes))
                {
                    deleted++;
                    freed += bytes;
                }
            }
            catch
            {
                // File vanished or is locked — ignore.
            }
        }

        var scope = olderThanDays > 0 ? $"artifacts older than {olderThanDays} day(s)" : "downloaded artifacts";
        LogCleanup(scope, deleted, freed, skipped);
        return new StorageCleanResult(deleted, freed, skipped);
    }

    /// <summary>
    /// Opt-in startup maintenance (Settings → Storage): deletes completed
    /// artifacts older than the configured number of days. Off by default.
    /// </summary>
    public static StorageCleanResult RunStartupCleanup(int olderThanDays)
    {
        if (olderThanDays <= 0) return new StorageCleanResult(0, 0, 0);
        return CleanCompletedArtifacts(olderThanDays);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static StorageFileKind Classify(string fileName)
    {
        if (fileName.EndsWith(".partial.meta", StringComparison.OrdinalIgnoreCase))
            return StorageFileKind.PartialMeta;
        if (fileName.EndsWith(".partial", StringComparison.OrdinalIgnoreCase))
            return StorageFileKind.Partial;
        return StorageFileKind.Artifact;
    }

    private static IEnumerable<string> EnumerateSafe(string folder)
    {
        if (!Directory.Exists(folder)) return Enumerable.Empty<string>();
        try
        {
            return Directory.EnumerateFiles(folder).ToList();
        }
        catch
        {
            return Enumerable.Empty<string>();
        }
    }

    private static bool TryDelete(string path, out long bytes)
    {
        bytes = 0;
        try
        {
            var info = new FileInfo(path);
            info.Delete();
            bytes = info.Length;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static long GetFolderSize(string folder)
    {
        if (!Directory.Exists(folder)) return 0;
        long total = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; } catch { }
            }
        }
        catch
        {
            // Partial result is fine for a size estimate.
        }
        return total;
    }

    private static void LogCleanup(string scope, int deleted, long freed, int skipped)
    {
        if (deleted == 0 && skipped == 0) return;

        var message = $"Storage cleanup removed {deleted} {scope} file(s), reclaiming {FormatBytes(freed)}";
        if (skipped > 0) message += $" ({skipped} in-flight transfer(s) left untouched)";

        ActivityLogService.Success("Storage", message);
        if (freed > 0)
        {
            NotificationCenterService.Post(
                NoteKind.Success, "Download cache cleaned",
                $"Reclaimed {FormatBytes(freed)} by removing {deleted} cached {scope}.");
        }
    }

    /// <summary>Formats a byte count as B / KB / MB / GB with one decimal above KB.</summary>
    public static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "0 B";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F0} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }
}
