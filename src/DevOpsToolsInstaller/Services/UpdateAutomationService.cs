using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsToolsInstaller.Models;

namespace DevOpsToolsInstaller.Services;

/// <summary>One discovered tool update candidate.</summary>
public sealed record DiscoveredToolUpdate(
    ToolDefinition Tool,
    string InstalledVersion,
    string AvailableVersion,
    string ReleaseNotesPreview,
    bool IsPinned,
    bool IsIgnored);

/// <summary>
/// Background update automation (feature 4).
///
/// Handles:
///   - Automated periodic background checks for installed tools
///   - Windows toast notifications with release notes preview
///   - Respecting pinned and ignored versions (via UpdatePreferenceService)
///   - Maintenance windows: optional automated quiet installations during off-hours
/// </summary>
public sealed class UpdateAutomationService : IDisposable
{
    private static UpdateAutomationService? _instance;
    public static UpdateAutomationService Instance => _instance ??= new UpdateAutomationService();

    private readonly Timer _checkTimer;
    private int _isChecking = 0;
    private DateTime? _lastCheckTime;
    private List<DiscoveredToolUpdate> _lastDiscovered = new();

    public event Action<IReadOnlyList<DiscoveredToolUpdate>>? UpdatesDiscovered;

    public DateTime? LastCheckTime => _lastCheckTime;
    public IReadOnlyList<DiscoveredToolUpdate> LastDiscoveredUpdates => _lastDiscovered;

    private UpdateAutomationService()
    {
        // First background check after 45 seconds, then repeats periodically
        var initialDelay = TimeSpan.FromSeconds(45);
        var interval = TimeSpan.FromHours(Math.Clamp(SettingsService.UpdateCheckIntervalHours, 1, 48));
        _checkTimer = new Timer(OnTimerTick, null, initialDelay, interval);
    }

    /// <summary>
    /// Reschedules the periodic timer based on current SettingsService configuration.
    /// </summary>
    public void RefreshTimerSchedule()
    {
        var interval = TimeSpan.FromHours(Math.Clamp(SettingsService.UpdateCheckIntervalHours, 1, 48));
        _checkTimer.Change(interval, interval);
    }

    private void OnTimerTick(object? state)
    {
        if (!SettingsService.AutoUpdateCheckEnabled) return;
        _ = RunCheckCycleAsync(isAutomatic: true);
    }

    /// <summary>
    /// Manually triggers an update check cycle.
    /// </summary>
    public Task<IReadOnlyList<DiscoveredToolUpdate>> CheckNowAsync()
    {
        return RunCheckCycleAsync(isAutomatic: false);
    }

    private async Task<IReadOnlyList<DiscoveredToolUpdate>> RunCheckCycleAsync(bool isAutomatic)
    {
        if (Interlocked.CompareExchange(ref _isChecking, 1, 0) != 0)
        {
            return _lastDiscovered;
        }

        try
        {
            _lastCheckTime = DateTime.Now;
            SettingsService.LastUpdateCheckTime = _lastCheckTime;

            var catalogService = new CatalogService();
            var catalog = await catalogService.LoadCatalogAsync();
            var dlFolder = DownloadService.DefaultDownloadsFolder;

            var installed = catalog
                .Where(t => UninstallService.IsInstalled(t, dlFolder) ||
                            File.Exists(Path.Combine(ArtifactService.BinFolder, t.FileName)))
                .ToList();

            var updates = new List<DiscoveredToolUpdate>();

            foreach (var tool in installed)
            {
                var curVer = ProjectManifestService.TryGetInstalledVersion(tool);
                if (string.IsNullOrWhiteSpace(curVer)) continue;

                var availVer = ProjectManifestService.NormalizeVersion(tool.Version);
                var normCur = ProjectManifestService.NormalizeVersion(curVer);

                bool isPinned = UpdatePreferenceService.IsPinned(tool.Id);
                bool isIgnored = UpdatePreferenceService.IsIgnored(tool.Id, tool.Version);

                // Check if catalog has a newer version
                if (!string.Equals(normCur, availVer, StringComparison.OrdinalIgnoreCase))
                {
                    string notesPreview = string.Empty;
                    try
                    {
                        var notes = await ToolReleaseNotesService.GetReleaseNotesAsync(tool.DownloadUrl);
                        if (notes != null && !string.IsNullOrWhiteSpace(notes.Body))
                        {
                            var firstLine = notes.Body.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                .FirstOrDefault(l => !l.StartsWith("#") && !string.IsNullOrWhiteSpace(l)) ?? "";
                            notesPreview = firstLine.Length > 120 ? firstLine[..120] + "…" : firstLine;
                        }
                    }
                    catch { }

                    updates.Add(new DiscoveredToolUpdate(
                        tool, curVer, tool.Version, notesPreview, isPinned, isIgnored));
                }
            }

            _lastDiscovered = updates;
            UiDispatcher.Run(() => UpdatesDiscovered?.Invoke(updates));

            var actionableUpdates = updates.Where(u => !u.IsPinned && !u.IsIgnored).ToList();

            if (actionableUpdates.Count == 0)
            {
                if (!isAutomatic)
                {
                    ToastService.Show("Tools Up-to-Date", "All installed tools match the latest catalog versions.");
                }
                return updates;
            }

            // Maintenance Window check: if enabled and inside the window, auto-install!
            if (SettingsService.MaintenanceWindowEnabled &&
                SettingsService.AutoInstallDuringMaintenance &&
                SettingsService.IsWithinMaintenanceWindow())
            {
                ActivityLogService.Info("Maintenance Window", $"Applying {actionableUpdates.Count} update(s) during maintenance window...");
                var dl = new DownloadService();
                int successCount = 0;

                foreach (var update in actionableUpdates)
                {
                    try
                    {
                        var t = update.Tool;
                        await dl.DownloadAsync(t, dlFolder);
                        if (t.Status == ToolStatus.Downloaded)
                        {
                            var res = ArtifactService.Perform(t, dlFolder);
                            if (res.Success) successCount++;
                        }
                    }
                    catch { }
                }

                var maintMsg = $"Maintenance window applied {successCount}/{actionableUpdates.Count} tool updates automatically.";
                ActivityLogService.Success("Maintenance Window", maintMsg);
                ToastService.Show("Maintenance Window Updates Complete", maintMsg);
                NotificationCenterService.Post(NoteKind.Update, "Maintenance Window Update", maintMsg);
            }
            else
            {
                // Surface Toast notification with release notes
                if (SettingsService.ShowUpdateToastWithReleaseNotes)
                {
                    if (actionableUpdates.Count == 1)
                    {
                        var u = actionableUpdates[0];
                        var note = string.IsNullOrWhiteSpace(u.ReleaseNotesPreview)
                            ? $"v{u.InstalledVersion} → v{u.AvailableVersion}"
                            : $"v{u.InstalledVersion} → v{u.AvailableVersion} • {u.ReleaseNotesPreview}";

                        ToastService.Show($"Update available: {u.Tool.Name}", note);
                        NotificationCenterService.Post(NoteKind.Update, $"Update: {u.Tool.Name}", note, u.Tool.Id);
                    }
                    else
                    {
                        var names = string.Join(", ", actionableUpdates.Take(3).Select(u => u.Tool.Name));
                        var extra = actionableUpdates.Count > 3 ? $" and {actionableUpdates.Count - 3} more" : "";
                        var msg = $"{actionableUpdates.Count} updates available ({names}{extra}). Click to review.";

                        ToastService.Show("Tool Updates Available", msg);
                        NotificationCenterService.Post(NoteKind.Update, "Tool Updates Available", msg);
                    }
                }
            }

            return updates;
        }
        catch (Exception ex)
        {
            ActivityLogService.Warn("Update Automation", $"Check failed: {ex.Message}");
            return _lastDiscovered;
        }
        finally
        {
            Interlocked.Exchange(ref _isChecking, 0);
        }
    }

    public void Dispose()
    {
        _checkTimer.Dispose();
    }
}
