using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DevOpsToolsInstaller.Models;

using DevOpsToolsInstaller.Helpers;

namespace DevOpsToolsInstaller.Services;

public sealed record SyncResult(bool Success, string Message, int FilesSynced);

public sealed record SyncStatus(string Folder, bool Configured, DateTime? LastSync, int LocalProfileCount);

/// <summary>
/// Cloud-synced workstation profiles via a user-chosen sync folder (roadmap:
/// "Cloud-synced workstation profiles across machines").
///
/// Point the sync folder at OneDrive / Dropbox / Google Drive (or any shared
/// folder) and the app merges profiles, custom stacks, hooks and snapshots
/// between machines — no account, no server, and zero telemetry. Merging is
/// conflict-aware: profiles resolve by most-recent update, snapshots by name.
/// </summary>
public static class ProfileSyncService
{
    private const string AppSubFolder = "DevOpsToolsInstaller";
    private const string LastSyncFileName = "last-sync.txt";

    public static bool IsConfigured
        => !string.IsNullOrWhiteSpace(SettingsService.SyncFolder);

    private static string LocalRoot => DevOpsToolsInstaller.Helpers.AppPaths.Root;

    private static string? SyncRoot
    {
        get
        {
            if (!IsConfigured) return null;
            return Path.Combine(SettingsService.SyncFolder, AppSubFolder);
        }
    }

    public static SyncStatus GetStatus()
    {
        var folder = SyncRoot ?? string.Empty;
        DateTime? last = null;

        try
        {
            var marker = Path.Combine(folder, LastSyncFileName);
            if (File.Exists(marker) && DateTime.TryParse(File.ReadAllText(marker), out var dt))
                last = dt;
        }
        catch { }

        return new SyncStatus(folder, IsConfigured, last, ProfileStoreService.LoadAll().Count);
    }

    /// <summary>
    /// Two-way merge: folds the sync folder into the local store, then pushes
    /// the merged result back. Safe to call repeatedly.
    /// </summary>
    public static SyncResult Sync()
    {
        if (!IsConfigured)
            return new SyncResult(false, "No sync folder configured. Choose one under Profiles & History → Sync.", 0);

        try
        {
            var root = SyncRoot!;
            Directory.CreateDirectory(root);

            int merged = 0;
            merged += MergeProfiles(root);
            merged += MergeSnapshots(root);
            merged += MergeHooks(root);
            merged += MergeCustomStacks(root);

            int pushed = PushFiles(root);

            File.WriteAllText(Path.Combine(root, LastSyncFileName), DateTime.Now.ToString("o"));
            SettingsService.LastSyncTime = DateTime.Now;
            SettingsService.SaveSettings();

            ActivityLogService.Success("Sync",
                $"Merged {merged} profile object(s) and pushed {pushed} file(s) to {root}.");
            return new SyncResult(true,
                $"Synced with {root}. Merged {merged} item(s), pushed {pushed} file(s).", pushed);
        }
        catch (Exception ex)
        {
            ActivityLogService.Error("Sync", $"Sync failed: {ex.Message}");
            return new SyncResult(false, $"Sync failed: {ex.Message}", 0);
        }
    }

    /// <summary>Pushes local state to the sync folder (local wins).</summary>
    public static SyncResult Push()
    {
        if (!IsConfigured)
            return new SyncResult(false, "No sync folder configured.", 0);
        try
        {
            var root = SyncRoot!;
            Directory.CreateDirectory(root);
            int pushed = PushFiles(root);
            File.WriteAllText(Path.Combine(root, LastSyncFileName), DateTime.Now.ToString("o"));
            return new SyncResult(true, $"Pushed {pushed} file(s) to {root}.", pushed);
        }
        catch (Exception ex)
        {
            return new SyncResult(false, $"Push failed: {ex.Message}", 0);
        }
    }

    /// <summary>Pulls the sync folder into local state (remote wins).</summary>
    public static SyncResult Pull()
    {
        if (!IsConfigured)
            return new SyncResult(false, "No sync folder configured.", 0);
        try
        {
            var root = SyncRoot!;
            if (!Directory.Exists(root))
                return new SyncResult(false, $"Sync folder not found: {root}", 0);

            int copied = 0;
            foreach (var name in SyncableFiles)
            {
                var src = Path.Combine(root, name);
                if (!File.Exists(src)) continue;
                File.Copy(src, Path.Combine(LocalRoot, name), overwrite: true);
                copied++;
            }

            var srcSnaps = Path.Combine(root, "snapshots");
            if (Directory.Exists(srcSnaps))
            {
                var destSnaps = Path.Combine(LocalRoot, "snapshots");
                Directory.CreateDirectory(destSnaps);
                foreach (var file in Directory.GetFiles(srcSnaps, "*.json"))
                {
                    File.Copy(file, Path.Combine(destSnaps, Path.GetFileName(file)), overwrite: true);
                    copied++;
                }
            }

            SettingsService.LastSyncTime = DateTime.Now;
            SettingsService.SaveSettings();
            return new SyncResult(true, $"Pulled {copied} file(s) from {root}.", copied);
        }
        catch (Exception ex)
        {
            return new SyncResult(false, $"Pull failed: {ex.Message}", 0);
        }
    }

    // ── Merge helpers ────────────────────────────────────────────────────

    private static readonly string[] SyncableFiles =
    {
        "profiles.json", "custom_stacks.json", "custom_tools.json", "hooks.json"
    };

    private static int PushFiles(string root)
    {
        int pushed = 0;
        foreach (var name in SyncableFiles)
        {
            var src = Path.Combine(LocalRoot, name);
            if (!File.Exists(src)) continue;
            File.Copy(src, Path.Combine(root, name), overwrite: true);
            pushed++;
        }

        var localSnaps = Path.Combine(LocalRoot, "snapshots");
        if (Directory.Exists(localSnaps))
        {
            var destSnaps = Path.Combine(root, "snapshots");
            Directory.CreateDirectory(destSnaps);
            foreach (var file in Directory.GetFiles(localSnaps, "*.json"))
            {
                File.Copy(file, Path.Combine(destSnaps, Path.GetFileName(file)), overwrite: true);
                pushed++;
            }
        }

        return pushed;
    }

    /// <summary>
    /// Builds an id-keyed map without throwing when the underlying store
    /// contains duplicate keys (hand-edited JSON, or a race between two
    /// syncing machines) — the last entry wins. A throwing ToDictionary here
    /// would abort the entire sync over one duplicate.
    /// </summary>
    private static Dictionary<string, T> ToTolerantDictionary<T>(
        IEnumerable<T> items, Func<T, string> keySelector, StringComparer comparer)
    {
        var map = new Dictionary<string, T>(comparer);
        foreach (var item in items)
        {
            var key = keySelector(item);
            if (string.IsNullOrWhiteSpace(key)) continue;
            map[key] = item;
        }
        return map;
    }

    private static int MergeProfiles(string root)
    {
        var remotePath = Path.Combine(root, "profiles.json");
        if (!File.Exists(remotePath)) return 0;

        var local = ProfileStoreService.LoadAll();
        var byId = ToTolerantDictionary(local, p => p.Id, StringComparer.OrdinalIgnoreCase);
        int merged = 0;

        foreach (var remote in ReadList<WorkstationProfile>(remotePath))
        {
            if (!byId.TryGetValue(remote.Id, out var existing))
            {
                byId[remote.Id] = remote;
                merged++;
            }
            else if (remote.UpdatedAt > existing.UpdatedAt)
            {
                byId[remote.Id] = remote;
                merged++;
            }
        }

        ProfileStoreService.ReplaceAll(byId.Values.ToList());
        return merged;
    }

    private static int MergeSnapshots(string root)
    {
        var remoteDir = Path.Combine(root, "snapshots");
        if (!Directory.Exists(remoteDir)) return 0;

        var local = ToTolerantDictionary(SnapshotService.LoadAll(), s => s.Name, StringComparer.OrdinalIgnoreCase);
        int merged = 0;

        foreach (var snap in ReadSnapshots(remoteDir))
        {
            if (!local.TryGetValue(snap.Name, out var existing) || snap.CreatedAt > existing.CreatedAt)
            {
                SnapshotService.Save(snap);
                local[snap.Name] = snap;
                merged++;
            }
        }

        return merged;
    }

    private static int MergeHooks(string root)
    {
        var remotePath = Path.Combine(root, "hooks.json");
        if (!File.Exists(remotePath)) return 0;

        var local = ToTolerantDictionary(HookService.LoadAll(), h => h.ToolId, StringComparer.OrdinalIgnoreCase);
        int merged = 0;

        foreach (var hook in ReadList<ToolHook>(remotePath))
        {
            if (!local.ContainsKey(hook.ToolId))
            {
                HookService.Save(hook);
                merged++;
            }
        }

        return merged;
    }

    private static int MergeCustomStacks(string root)
    {
        var remotePath = Path.Combine(root, "custom_stacks.json");
        if (!File.Exists(remotePath)) return 0;

        var local = ToTolerantDictionary(CustomStacksService.LoadCustomStacks(), b => b.Id, StringComparer.OrdinalIgnoreCase);
        int merged = 0;

        foreach (var stack in ReadList<ToolBundle>(remotePath))
        {
            if (!string.IsNullOrWhiteSpace(stack.Id) && !local.ContainsKey(stack.Id))
            {
                CustomStacksService.SaveCustomStack(stack);
                merged++;
            }
        }

        return merged;
    }

    private static List<T> ReadList<T>(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<T>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<T>();
        }
        catch
        {
            return new List<T>();
        }
    }

    private static List<WorkstationSnapshot> ReadSnapshots(string dir)
    {
        var list = new List<WorkstationSnapshot>();
        foreach (var file in Directory.GetFiles(dir, "*.json"))
        {
            try
            {
                var snap = JsonSerializer.Deserialize<WorkstationSnapshot>(
                    File.ReadAllText(file),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (snap is not null) list.Add(snap);
            }
            catch { }
        }
        return list;
    }
}
