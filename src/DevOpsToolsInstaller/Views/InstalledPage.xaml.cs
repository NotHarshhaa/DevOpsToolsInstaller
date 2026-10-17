using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DevOpsToolsInstaller.Helpers;
using DevOpsToolsInstaller.Models;
using DevOpsToolsInstaller.Services;

namespace DevOpsToolsInstaller.Views;

public sealed partial class InstalledPage : Page, IRefreshablePage
{
    private List<ToolDefinition> _allInstalledTools = new();

    /// <summary>The rows currently bound to the list, in display order.</summary>
    private List<ToolDefinition> _visibleTools = new();

    /// <summary>Tools whose <c>PropertyChanged</c> is currently subscribed.</summary>
    private readonly HashSet<ToolDefinition> _hookedTools = new();

    /// <summary>Active segment of the All / Updates / Not on PATH filter.</summary>
    private string _statusFilter = "All";

    /// <summary>Active sort key: "name", "status" or "version".</summary>
    private string _sortColumn = "name";
    private bool _sortDescending;

    /// <summary>
    /// Set while a tool's preference state is being refreshed, so the burst of
    /// notifications that model raises is collapsed into a single re-filter.
    /// </summary>
    private bool _suppressAutoRefresh;

    /// <summary>True while a coalesced re-filter is already queued.</summary>
    private bool _filterRefreshQueued;

    private DateTime _lastChecked = DateTime.Now;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _searchDebounceTimer;
    private CancellationTokenSource? _scanCts;

    public InstalledPage()
    {
        InitializeComponent();

        // Wired here rather than in XAML: the control raises SelectionChanged
        // while its items are being parsed, which would fire before the page has
        // any state to apply the filter against.
        FilterSegmented.SelectionChanged += FilterSegmented_SelectionChanged;

        UpdateSortGlyphs();

        Loaded += InstalledPage_Loaded;
        Unloaded += InstalledPage_Unloaded;
        ActualThemeChanged += (s, e) =>
        {
            if (_latencyNames.Count > 0)
            {
                RenderLatencyChart(_latencyNames.ToArray(), _latencyValuesMs.ToArray());
            }
        };
    }

    // ── CLI health-check latency chart ──────────────────────────────────

    private readonly List<string> _latencyNames = new();
    private readonly List<double> _latencyValuesMs = new();

    public System.Collections.ObjectModel.ObservableCollection<ISeries> LatencySeries { get; private set; } = new();
    public System.Collections.Generic.IEnumerable<LiveChartsCore.Kernel.Sketches.ICartesianAxis> LatencyXAxes { get; private set; } = ChartTelemetry.HiddenAxes();
    public System.Collections.Generic.IEnumerable<LiveChartsCore.Kernel.Sketches.ICartesianAxis> LatencyYAxes { get; private set; } = ChartTelemetry.HiddenAxes();

    private void ResetLatencySamples()
    {
        _latencyNames.Clear();
        _latencyValuesMs.Clear();
    }

    /// <summary>Builds the per-tool latency bar chart and shows it (or the empty state).</summary>
    private void RenderLatencyChart(string[] names, double[] valuesMs)
    {
        if (names.Length == 0)
        {
            LatencySeries = new System.Collections.ObjectModel.ObservableCollection<ISeries>();
            LatencyChartCard.Visibility = Visibility.Collapsed;
            LatencyEmptyState.Visibility = Visibility.Visible;
            Bindings.Update();
            return;
        }

        var values = new ChartTelemetry.BoundedValues(Math.Max(valuesMs.Length, 2));
        foreach (var ms in valuesMs)
        {
            values.Push(ms);
        }

        LatencySeries = new System.Collections.ObjectModel.ObservableCollection<ISeries>
        {
            ChartTelemetry.CreateBarSeries(values)
        };
        LatencyXAxes = ChartTelemetry.CreateLabeledAxes(names, ActualTheme, labelsRotation: 55);
        LatencyYAxes = ChartTelemetry.CreateSparkAxes(ActualTheme);

        LatencyEmptyState.Visibility = Visibility.Collapsed;
        LatencyChartCard.Visibility = Visibility.Visible;
        Bindings.Update();
    }

    private void CloseLatencyChart_Click(object sender, RoutedEventArgs e)
    {
        LatencyChartCard.Visibility = Visibility.Collapsed;
    }

    private async void InstalledPage_Loaded(object sender, RoutedEventArgs e)
    {
        await ScanInstalledToolsAsync();
    }

    private void InstalledPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = null;

        // The page is cached by the Frame, so drop the per-tool subscriptions
        // instead of keeping every installed tool alive across navigations.
        foreach (var tool in _hookedTools)
        {
            tool.PropertyChanged -= Tool_PropertyChanged;
        }
        _hookedTools.Clear();
    }

    /// <summary>One installed tool as found by the background scan.</summary>
    private readonly record struct InstalledScanRow(
        ToolDefinition Tool,
        string? RegisteredVersion,
        bool IsOnPath);

    /// <summary>
    /// Registry/filesystem probe for installed tools, run off the UI thread.
    /// Only plain data is produced here: the flags are applied to the models back
    /// on the UI thread so bindings never update from a background thread.
    /// </summary>
    private async Task ScanInstalledToolsAsync()
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        // Show the loading overlay while the catalog hydrates and the
        // workstation scan runs, so the table doesn't appear empty.
        InstalledLoading.Visibility = Visibility.Visible;
        InstalledList.Visibility = Visibility.Collapsed;
        EmptyState.Visibility = Visibility.Collapsed;

        StatusText.Text = "Scanning installed tools…";
        await mw.EnsureCatalogLoadedAsync();

        var dlFolder = DownloadService.DefaultDownloadsFolder;

        var detected = await Task.Run(() =>
        {
            var results = new List<InstalledScanRow>();
            foreach (var tool in mw.Tools)
            {
                if (!UninstallService.IsInstalled(tool, dlFolder)) continue;

                // Installers keep their real version in the registry; everything
                // else is resolved later by probing the CLI itself.
                var registeredVersion = tool.Kind == ArtifactKind.Installer
                    ? UninstallService.GetInstalledVersion(tool)
                    : null;

                results.Add(new InstalledScanRow(
                    tool,
                    registeredVersion,
                    CliHealthService.IsToolOnPath(tool)));
            }
            return results;
        });

        _allInstalledTools = new List<ToolDefinition>(detected.Count);
        foreach (var row in detected)
        {
            var tool = row.Tool;
            tool.IsInstalled = true;
            if (!string.IsNullOrWhiteSpace(row.RegisteredVersion))
            {
                tool.DetectedVersion = row.RegisteredVersion;
            }
            tool.IsOnPath = row.IsOnPath;

            _allInstalledTools.Add(tool);

            if (_hookedTools.Add(tool))
            {
                tool.PropertyChanged += Tool_PropertyChanged;
            }
        }

        // Tools that were uninstalled since the last scan are gone from the list;
        // stop listening to them.
        foreach (var stale in _hookedTools.Where(t => !_allInstalledTools.Contains(t)).ToList())
        {
            stale.PropertyChanged -= Tool_PropertyChanged;
            _hookedTools.Remove(stale);
        }

        _lastChecked = DateTime.Now;

        var count = _allInstalledTools.Count;
        Header.BadgeText = $"{count} deployed";
        Header.Title = $"Installed Tools & Updates ({count} total)";

        ApplyFilter();

        // Reveal the table (or empty state) now that the scan has populated it.
        InstalledLoading.Visibility = Visibility.Collapsed;
        InstalledList.Visibility = _allInstalledTools.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (count == 0)
        {
            StatusText.Text = "No installed tools found";
            return;
        }

        StatusText.Text = $"{count} tool(s) detected on your workstation";

        // Asynchronously resolve CLI versions in the background with bounded
        // concurrency (3 parallel). Results are marshalled back onto the UI thread.
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;

        _ = Task.Run(async () =>
        {
            using var sem = new System.Threading.SemaphoreSlim(3, 3);
            var unverified = _allInstalledTools.Where(t => string.IsNullOrWhiteSpace(t.DetectedVersion)).ToList();
            var tasks = unverified.Select(async tool =>
            {
                // Only release the slot that was actually taken: a cancelled
                // WaitAsync never acquired one, and releasing anyway would push
                // the semaphore past its maximum.
                var acquired = false;
                try
                {
                    await sem.WaitAsync(token);
                    acquired = true;

                    if (token.IsCancellationRequested) return;

                    var probe = await CliHealthService.ProbeToolAsync(tool, timeoutSeconds: 2);
                    if (probe.Success && !string.IsNullOrWhiteSpace(probe.DetectedVersion))
                    {
                        _ = DispatcherQueue.TryEnqueue(() =>
                        {
                            tool.DetectedVersion = probe.DetectedVersion;
                            tool.HealthStatus = "Healthy";
                            tool.HealthOutput = probe.RawOutput;
                        });
                    }
                }
                catch { }
                finally
                {
                    if (acquired) sem.Release();
                }
            });

            try
            {
                await Task.WhenAll(tasks);
            }
            catch { }
        }, token);
    }

    // ── List state: filter, sort, empty states ──────────────────────────

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            _searchDebounceTimer?.Stop();
            _searchDebounceTimer = DispatcherQueue.CreateTimer();
            _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(150);
            _searchDebounceTimer.Tick += (s, e) =>
            {
                _searchDebounceTimer?.Stop();
                ApplyFilter();
            };
            _searchDebounceTimer.Start();
        }
    }

    private void FilterSegmented_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not CommunityToolkit.WinUI.Controls.Segmented segmented ||
            segmented.SelectedItem is not CommunityToolkit.WinUI.Controls.SegmentedItem selected)
        {
            return;
        }

        _statusFilter = selected.Tag as string ?? "All";
        ApplyFilter();
    }

    private void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        SelectFilterSegment("All");

        // The segment change re-enters ApplyFilter; this covers the case where
        // "All" was already the active segment and no event is raised.
        _statusFilter = "All";
        ApplyFilter();
    }

    /// <summary>Moves the segmented filter to the item carrying the given tag.</summary>
    private void SelectFilterSegment(string tag)
    {
        foreach (var item in FilterSegmented.Items.OfType<CommunityToolkit.WinUI.Controls.SegmentedItem>())
        {
            if (string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                FilterSegmented.SelectedItem = item;
                return;
            }
        }
    }

    /// <summary>Rebuilds the visible rows from the search text, the status filter and the sort key.</summary>
    private void ApplyFilter()
    {
        var query = SearchBox.Text?.Trim() ?? string.Empty;

        var filtered = _allInstalledTools.AsEnumerable();

        if (!string.IsNullOrEmpty(query))
        {
            filtered = filtered.Where(t =>
                t.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.Category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(t.DetectedVersion) && t.DetectedVersion.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }

        filtered = _statusFilter switch
        {
            "Updates" => filtered.Where(t => t.HasActionableUpdate),
            "NotOnPath" => filtered.Where(t => !t.IsOnPath),
            _ => filtered
        };

        var next = SortRows(filtered).ToList();

        // Re-assigning ItemsSource re-realises every row and replays the entrance
        // transition, so only do it when the membership or order actually changed.
        // Row *contents* still update live through their own bindings, which keeps
        // scroll position stable while versions are being probed.
        if (!next.SequenceEqual(_visibleTools))
        {
            _visibleTools = next;
            InstalledList.ItemsSource = _visibleTools;
        }

        UpdateEmptyStates();

        if (UpdateAllButton is not null)
        {
            UpdateAllButton.IsEnabled = _allInstalledTools.Any(t => t.HasActionableUpdate);
        }

        // The footer summary owns the "how many rows" fact so it is always
        // accurate. StatusText is left alone here: it carries whatever the last
        // action reported, and re-filtering must not wipe that message a moment
        // after it appears.
        if (FooterStatusText != null)
        {
            FooterStatusText.Text = $"{_visibleTools.Count} of {_allInstalledTools.Count} tool(s) shown • Catalog: {CatalogService.CatalogFreshness} • Last checked {GetLastCheckedText()}.";
        }

        UpdateBulkBar();
    }

    private void UpdateEmptyStates()
    {
        if (EmptyState is null || NoMatchesState is null || InstalledList is null) return;

        var nothingInstalled = _allInstalledTools.Count == 0;
        var nothingMatches = !nothingInstalled && _visibleTools.Count == 0;

        EmptyState.Visibility = nothingInstalled ? Visibility.Visible : Visibility.Collapsed;
        NoMatchesState.Visibility = nothingMatches ? Visibility.Visible : Visibility.Collapsed;
        InstalledList.Visibility = _visibleTools.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private IEnumerable<ToolDefinition> SortRows(IEnumerable<ToolDefinition> rows) => _sortColumn switch
    {
        "status" => _sortDescending
            ? rows.OrderByDescending(StatusRank).ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
            : rows.OrderBy(StatusRank).ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase),

        "version" => _sortDescending
            ? rows.OrderByDescending(t => t.DetectedVersion, VersionSortComparer.Instance)
            : rows.OrderBy(t => t.DetectedVersion, VersionSortComparer.Instance),

        _ => _sortDescending
            ? rows.OrderByDescending(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
            : rows.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
    };

    /// <summary>
    /// Sort weight for the Status column: rows needing attention float to the top
    /// in the order the chips are shown, ordinary rows sink to the bottom.
    /// </summary>
    private static int StatusRank(ToolDefinition tool) =>
        tool.HasActionableUpdate ? 0
        : !tool.IsOnPath ? 1
        : tool.IsNewerThanCatalog ? 2
        : 3;

    /// <summary>
    /// Orders versions numerically when both sides parse (so 1.10.0 beats 1.9.9)
    /// and falls back to text order otherwise, with unknown versions last.
    /// </summary>
    private sealed class VersionSortComparer : IComparer<string?>
    {
        public static readonly VersionSortComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            var hasX = TryParse(x, out var vx);
            var hasY = TryParse(y, out var vy);

            if (hasX && hasY) return vx.CompareTo(vy);
            if (hasX) return -1;   // a known version sorts before an unknown one
            if (hasY) return 1;
            return string.Compare(x, y, StringComparison.CurrentCultureIgnoreCase);
        }

        private static bool TryParse(string? value, out System.Version version)
        {
            version = new System.Version(0, 0);
            if (string.IsNullOrWhiteSpace(value)) return false;

            var match = System.Text.RegularExpressions.Regex.Match(value, @"\b(\d+(\.\d+){1,3})\b");
            var candidate = match.Success ? match.Groups[1].Value : value.Trim().TrimStart('v', 'V');

            if (System.Version.TryParse(candidate, out var parsed) && parsed is not null)
            {
                version = parsed;
                return true;
            }

            return false;
        }
    }

    private string GetLastCheckedText()
    {
        var elapsed = DateTime.Now - _lastChecked;
        if (elapsed.TotalMinutes < 1) return "Just now";
        if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes} min ago";
        return $"{(int)elapsed.TotalHours} hr ago";
    }

    // ── Column sorting ──────────────────────────────────────────────────

    private void SortByName_Click(object sender, RoutedEventArgs e) => ToggleSort("name");
    private void SortByStatus_Click(object sender, RoutedEventArgs e) => ToggleSort("status");
    private void SortByVersion_Click(object sender, RoutedEventArgs e) => ToggleSort("version");

    /// <summary>Sorts by a column, or flips the direction when it is already active.</summary>
    private void ToggleSort(string column)
    {
        if (string.Equals(_sortColumn, column, StringComparison.Ordinal))
        {
            _sortDescending = !_sortDescending;
        }
        else
        {
            _sortColumn = column;
            _sortDescending = false;
        }

        UpdateSortGlyphs();
        ApplyFilter();
    }

    /// <summary>Points the chevron at the active column and hides the others.</summary>
    private void UpdateSortGlyphs()
    {
        if (SortGlyphName is null || SortGlyphStatus is null || SortGlyphVersion is null) return;

        // Chevron up for ascending, down for descending (matches Windows lists).
        var glyph = _sortDescending ? "\uE70D" : "\uE70E";

        SortGlyphName.Visibility = _sortColumn == "name" ? Visibility.Visible : Visibility.Collapsed;
        SortGlyphStatus.Visibility = _sortColumn == "status" ? Visibility.Visible : Visibility.Collapsed;
        SortGlyphVersion.Visibility = _sortColumn == "version" ? Visibility.Visible : Visibility.Collapsed;

        SortGlyphName.Glyph = glyph;
        SortGlyphStatus.Glyph = glyph;
        SortGlyphVersion.Glyph = glyph;
    }

    // ── Row selection & bulk actions ────────────────────────────────────

    private void Tool_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // A few properties are still set from background probing; make sure every
        // handler runs on the UI thread before it touches the visual tree.
        if (!DispatcherQueue.HasThreadAccess)
        {
            _ = DispatcherQueue.TryEnqueue(() => Tool_PropertyChanged(sender, e));
            return;
        }

        if (_suppressAutoRefresh) return;

        if (e.PropertyName is nameof(ToolDefinition.IsSelected))
        {
            UpdateBulkBar();
            return;
        }

        // A row's state changed (update appeared or was applied, PATH fixed,
        // version detected): re-evaluate the filter so the Updates and Not on
        // PATH views never show stale membership. Probes finish in bursts, so the
        // work is coalesced into a single pass instead of one per notification.
        if (e.PropertyName is nameof(ToolDefinition.HasActionableUpdate)
            or nameof(ToolDefinition.IsOnPath)
            or nameof(ToolDefinition.IsNewerThanCatalog)
            or nameof(ToolDefinition.VersionColumnText)
            or nameof(ToolDefinition.IsVersionPinned)
            or nameof(ToolDefinition.IsUpdateIgnored))
        {
            QueueApplyFilter();
        }
    }

    /// <summary>
    /// Queues one re-filter on the dispatcher, collapsing a burst of property
    /// notifications (a version-probe sweep, a bulk selection) into a single pass.
    /// </summary>
    private void QueueApplyFilter()
    {
        if (_filterRefreshQueued) return;
        _filterRefreshQueued = true;

        _ = DispatcherQueue.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () =>
            {
                _filterRefreshQueued = false;
                ApplyFilter();
            });
    }

    /// <summary>
    /// Re-reads a tool's pin/ignore state, then re-applies the view exactly once
    /// instead of once per property the model notifies.
    /// </summary>
    private void RefreshRowState(ToolDefinition tool)
    {
        _suppressAutoRefresh = true;
        try
        {
            tool.RefreshUpdatePreferences();
        }
        finally
        {
            _suppressAutoRefresh = false;
        }

        ApplyFilter();
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        var select = SelectAllCheckBox.IsChecked == true;

        // One bulk bar update at the end instead of one per row notification.
        _suppressAutoRefresh = true;
        try
        {
            foreach (var tool in _visibleTools)
            {
                tool.IsSelected = select;
            }
        }
        finally
        {
            _suppressAutoRefresh = false;
        }

        UpdateBulkBar();
    }

    private void SelectionClear_Click(object sender, RoutedEventArgs e)
    {
        _suppressAutoRefresh = true;
        try
        {
            foreach (var tool in _allInstalledTools.Where(t => t.IsSelected).ToList())
            {
                tool.IsSelected = false;
            }
        }
        finally
        {
            _suppressAutoRefresh = false;
        }

        UpdateBulkBar();
    }

    /// <summary>
    /// Drives the floating bulk bar from the current selection. The selection is
    /// the whole installed set rather than just the visible rows, so a choice made
    /// under one filter is not silently dropped when the filter changes.
    /// </summary>
    private void UpdateBulkBar()
    {
        if (SelectionBar is null) return;

        var selected = _allInstalledTools.Where(t => t.IsSelected).ToList();

        // Keep the header checkbox truthful: checked only when every visible row
        // is selected, and disabled when a filter has hidden everything. Setting
        // IsChecked raises no Click, so this cannot loop back into the handler.
        if (SelectAllCheckBox is not null)
        {
            SelectAllCheckBox.IsEnabled = _visibleTools.Count > 0;
            SelectAllCheckBox.IsChecked =
                _visibleTools.Count > 0 && _visibleTools.All(t => t.IsSelected);
        }

        if (selected.Count == 0)
        {
            SelectionBar.Visibility = Visibility.Collapsed;
            InstalledList.Padding = new Thickness(0, 0, 0, 12);
            return;
        }

        var updatable = selected.Count(t => t.HasActionableUpdate);
        var removable = selected.Count(t => t.SupportsUninstall);

        SelectionCountText.Text = selected.Count == 1 ? "1 selected" : $"{selected.Count} selected";

        SelectionUpdateButton.IsEnabled = updatable > 0;
        SelectionUpdateButton.Opacity = updatable > 0 ? 1 : 0.5;
        ToolTipService.SetToolTip(SelectionUpdateButton, updatable > 0
            ? $"Install the newer version of {updatable} selected tool(s)"
            : "No selected tool has an update available");

        SelectionUninstallButton.IsEnabled = removable > 0;
        SelectionUninstallButton.Opacity = removable > 0 ? 1 : 0.5;
        ToolTipService.SetToolTip(SelectionUninstallButton, removable > 0
            ? $"Remove {removable} selected tool(s) from this workstation"
            : "None of the selected tools can be removed automatically");

        SelectionBar.Visibility = Visibility.Visible;

        // Reserve room so the floating bar never hides the last row.
        InstalledList.Padding = new Thickness(0, 0, 0, 88);
    }

    // ── Update preferences (pin / ignore) ───────────────────────────────

    private void PinVersion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ToolDefinition tool }) return;

        if (tool.IsVersionPinned)
        {
            UpdatePreferenceService.Unpin(tool.Id);
            StatusText.Text = $"{tool.Name} is no longer pinned; updates are offered again.";
            RefreshRowState(tool);
            return;
        }

        if (string.IsNullOrWhiteSpace(tool.DetectedVersion))
        {
            StatusText.Text = $"Cannot pin {tool.Name}: the installed version could not be detected.";
            return;
        }

        UpdatePreferenceService.Pin(tool.Id, tool.DetectedVersion);
        StatusText.Text = $"{tool.Name} pinned to v{tool.DetectedVersion} — it will not be offered updates.";
        RefreshRowState(tool);
    }

    private void IgnoreUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ToolDefinition tool }) return;

        if (tool.IsUpdateIgnored)
        {
            UpdatePreferenceService.ClearIgnoredUpdate(tool.Id);
            StatusText.Text = $"{tool.Name} will be told about the next release.";
        }
        else
        {
            UpdatePreferenceService.IgnoreUpdate(tool.Id, tool.Version);
            StatusText.Text = $"Ignoring v{tool.Version} for {tool.Name} — a later release will still be offered.";
        }

        RefreshRowState(tool);
    }

    // ── Updating ────────────────────────────────────────────────────────

    private void UpdateTool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ToolDefinition tool }) return;
        RunUpdates(new[] { tool });
    }

    private void UpdateAll_Click(object sender, RoutedEventArgs e)
    {
        var targets = _allInstalledTools.Where(t => t.HasActionableUpdate).ToList();
        if (targets.Count == 0)
        {
            StatusText.Text = "Every installed tool is already on its newest version.";
            return;
        }

        RunUpdates(targets);
    }

    private void BulkUpdate_Click(object sender, RoutedEventArgs e)
    {
        var targets = _allInstalledTools.Where(t => t.IsSelected && t.HasActionableUpdate).ToList();
        if (targets.Count == 0)
        {
            StatusText.Text = "None of the selected tools has an update available.";
            return;
        }

        RunUpdates(targets);
    }

    /// <summary>
    /// Queues the given tools for download-and-install and sends the user to the
    /// Downloads page, where progress is reported for every item.
    /// </summary>
    private void RunUpdates(IReadOnlyList<ToolDefinition> targets)
    {
        var mw = App.MainWindowInstance;
        if (mw is null || targets.Count == 0) return;

        StatusText.Text = targets.Count == 1
            ? $"Updating {targets[0].Name}…"
            : $"Updating {targets.Count} tools…";

        foreach (var tool in targets)
        {
            if (!mw.DownloadQueue.Contains(tool))
            {
                mw.DownloadQueue.Add(tool);
            }
        }

        if (targets.Count > 1)
        {
            ToastService.Show("Updating tools", $"Downloading and installing {targets.Count} updates.");
        }

        _ = Task.Run(async () =>
        {
            // Bounded concurrency keeps the network and the disk busy without
            // stampeding either; three matches the install scan's own limit.
            using var gate = new System.Threading.SemaphoreSlim(3, 3);
            await Task.WhenAll(targets.Select(async tool =>
            {
                await gate.WaitAsync();
                try
                {
                    await UpdateSingleToolAsync(tool);
                }
                finally
                {
                    gate.Release();
                }
            }));
        });

        mw.NavigateTo("Downloads");
    }

    /// <summary>Downloads one tool and applies the artifact. Safe to run in parallel.</summary>
    private async Task UpdateSingleToolAsync(ToolDefinition tool)
    {
        var mw = App.MainWindowInstance;
        if (mw is null) return;

        try
        {
            var dlFolder = DownloadService.DefaultDownloadsFolder;
            await mw.DownloadSvc.DownloadAsync(tool, dlFolder);

            if (tool.Status != ToolStatus.Downloaded)
            {
                _ = mw.DispatcherQueue.TryEnqueue(() =>
                    StatusText.Text = $"{tool.Name}: download did not complete.");
                return;
            }

            var result = ArtifactService.Perform(tool, dlFolder);

            if (result.Success)
            {
                ActivityLogService.Success(tool.Name, result.Message);
            }
            else
            {
                ActivityLogService.Error(tool.Name, result.Message);
            }

            _ = mw.DispatcherQueue.TryEnqueue(() =>
            {
                StatusText.Text = $"{tool.Name}: {result.Message}";
                // An applied update clears any dismissal recorded against the
                // release that is now installed.
                if (result.Success)
                {
                    UpdatePreferenceService.ClearIgnoredUpdate(tool.Id);
                    tool.RefreshUpdatePreferences();
                }
            });

            ToastService.Show(
                result.Success ? $"{tool.Name} updated" : $"{tool.Name} update failed",
                result.Message);
        }
        catch (Exception ex)
        {
            ActivityLogService.Error(tool.Name, $"Update failed: {ex.Message}");
            _ = mw.DispatcherQueue.TryEnqueue(() =>
                StatusText.Text = $"Update failed for {tool.Name}: {ex.Message}");
        }
    }

    private void LaunchCli_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ToolDefinition tool }) return;

        try
        {
            var (exeName, _) = CliHealthService.GetProbeCommand(tool);
            var exePath = CliHealthService.ResolveExecutablePath(exeName, tool.Id);
            var cmdTarget = !string.IsNullOrEmpty(exePath) ? exePath : exeName;

            var cmd = $"Write-Host '--- {tool.Name} Terminal Session ---' -ForegroundColor Cyan; if (Get-Command '{cmdTarget}' -ErrorAction SilentlyContinue) {{ & '{cmdTarget}' --help }} else {{ Write-Host 'Run {tool.Name} command:' -ForegroundColor Yellow }}";

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoExit -Command \"{cmd}\"",
                UseShellExecute = true
            };

            try
            {
                var wtPsi = new ProcessStartInfo
                {
                    FileName = "wt.exe",
                    Arguments = $"powershell.exe -NoExit -Command \"{cmd}\"",
                    UseShellExecute = true
                };
                Process.Start(wtPsi);
            }
            catch
            {
                Process.Start(psi);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not launch CLI: {ex.Message}";
        }
    }

    private void OpenSingleToolFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { DataContext: ToolDefinition tool }) return;
        var (exeName, _) = CliHealthService.GetProbeCommand(tool);
        var path = CliHealthService.ResolveExecutablePath(exeName, tool.Id);
        var dir = !string.IsNullOrEmpty(path) && File.Exists(path)
            ? Path.GetDirectoryName(path)
            : DownloadService.DefaultDownloadsFolder;

        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
        {
            Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = dir, UseShellExecute = true });
        }
    }

    private void CopyToolPath_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { DataContext: ToolDefinition tool }) return;
        var (exeName, _) = CliHealthService.GetProbeCommand(tool);
        var path = CliHealthService.ResolveExecutablePath(exeName, tool.Id) ?? exeName;
        var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
        dp.SetText(path);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
        StatusText.Text = $"Copied path to clipboard: {path}";
    }

    private void OpenDoc_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { DataContext: ToolDefinition tool }) return;
        if (!string.IsNullOrWhiteSpace(tool.Homepage))
        {
            LauncherService.OpenUrl(tool.Homepage);
        }
    }

    private async void CheckSingleToolUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ToolDefinition tool }) return;

        StatusText.Text = $"Checking {tool.Name}…";
        var probe = await CliHealthService.ProbeToolAsync(tool);
        if (probe.Success && !string.IsNullOrWhiteSpace(probe.DetectedVersion))
        {
            tool.DetectedVersion = probe.DetectedVersion;
            tool.HealthStatus = "Healthy";
        }

        tool.IsOnPath = CliHealthService.IsToolOnPath(tool);

        StatusText.Text = $"{tool.Name}: {tool.VersionStateText} • Catalog: {CatalogService.CatalogFreshness}";
        ApplyFilter();
    }

    private async void TestCli_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ToolDefinition tool }) return;

        StatusText.Text = $"Testing {tool.Name}…";
        tool.HealthStatus = "Testing…";

        var result = await CliHealthService.ProbeToolAsync(tool);
        tool.HealthStatus = result.Success ? "Healthy" : "Failed";
        tool.HealthOutput = result.RawOutput;

        if (result.Success && !string.IsNullOrWhiteSpace(result.DetectedVersion))
        {
            tool.DetectedVersion = result.DetectedVersion;
        }

        tool.IsOnPath = CliHealthService.IsToolOnPath(tool);
        StatusText.Text = $"{tool.Name}: {result.Message}";

        // Show detailed health report dialog
        var reportBox = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(result.RawOutput) ? "(No output returned)" : result.RawOutput,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 12,
            Height = 180
        };

        var contentPanel = new StackPanel { Spacing = 10 };
        contentPanel.Children.Add(new TextBlock
        {
            Text = $"Status: {(result.Success ? "Healthy (Pass)" : "Failed / Unresponsive")}",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = result.Success
                ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorSuccessBrush"]
                : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCautionBrush"]
        });
        contentPanel.Children.Add(new TextBlock
        {
            Text = $"Command: {result.CommandRun}  ({result.ElapsedMilliseconds} ms latency)",
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });
        contentPanel.Children.Add(reportBox);

        var dialog = new ContentDialog
        {
            Title = $"{tool.Name} Health Check",
            Content = contentPanel,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        await dialog.ShowAsync();
    }

    private async void RunAllHealthChecks_Click(object sender, RoutedEventArgs e)
    {
        if (_allInstalledTools.Count == 0)
        {
            StatusText.Text = "No installed tools to check.";
            return;
        }

        StatusText.Text = $"Running health checks on {_allInstalledTools.Count} tools…";
        int healthyCount = 0;
        ResetLatencySamples();

        foreach (var tool in _allInstalledTools)
        {
            tool.HealthStatus = "Testing…";
            var result = await CliHealthService.ProbeToolAsync(tool);
            tool.HealthStatus = result.Success ? "Healthy" : "Failed";
            tool.HealthOutput = result.RawOutput;
            if (result.Success)
            {
                healthyCount++;
                _latencyNames.Add(tool.Name);
                _latencyValuesMs.Add(result.ElapsedMilliseconds);
                if (!string.IsNullOrWhiteSpace(result.DetectedVersion))
                {
                    tool.DetectedVersion = result.DetectedVersion;
                }
            }
        }

        // Latency chart: successful probes only; empty state when none ran.
        RenderLatencyChart(_latencyNames.ToArray(), _latencyValuesMs.ToArray());
        ApplyFilter();

        StatusText.Text = $"Health check complete: {healthyCount}/{_allInstalledTools.Count} CLIs healthy.";
    }

    private async void ShellCompletion_Click(object sender, RoutedEventArgs e)
    {
        var mw = App.MainWindowInstance;
        var installedTools = _allInstalledTools.Count > 0 ? _allInstalledTools : (mw?.Tools.Where(t => t.IsInstalled).ToList() ?? new List<ToolDefinition>());

        await ShowShellCompletionModalAsync(installedTools);
    }

    public async Task ShowShellCompletionModalAsync(IReadOnlyList<ToolDefinition> tools)
    {
        var psSnippet = ShellCompletionService.GeneratePowerShellSnippet(tools);
        var bashSnippet = ShellCompletionService.GenerateBashSnippet(tools);

        var snippetBox = new TextBox
        {
            Text = psSnippet,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
            FontSize = 12,
            Height = 220
        };

        var statusLabel = new TextBlock
        {
            Text = ShellCompletionService.IsSnippetInProfile()
                ? "Active in your PowerShell profile ($PROFILE)"
                : "Not yet configured in your PowerShell profile",
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        };

        var psRadio = new RadioButton { Content = "PowerShell ($PROFILE)", IsChecked = true, Margin = new Thickness(0, 0, 12, 0) };
        var bashRadio = new RadioButton { Content = "Bash / Zsh (WSL / Git Bash)" };

        var radioPanel = new StackPanel { Orientation = Orientation.Horizontal };
        radioPanel.Children.Add(psRadio);
        radioPanel.Children.Add(bashRadio);

        var saveProfileBtn = new Button
        {
            Content = "Append to $PROFILE",
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            CornerRadius = new CornerRadius(4)
        };
        saveProfileBtn.Click += (s, e) =>
        {
            var (success, msg) = ShellCompletionService.ApplyToPowerShellProfile(psSnippet);
            statusLabel.Text = msg;
            if (success)
            {
                saveProfileBtn.IsEnabled = false;
                saveProfileBtn.Content = "Saved to $PROFILE";
            }
        };

        psRadio.Checked += (s, e) =>
        {
            snippetBox.Text = psSnippet;
            saveProfileBtn.Visibility = Visibility.Visible;
        };
        bashRadio.Checked += (s, e) =>
        {
            snippetBox.Text = bashSnippet;
            saveProfileBtn.Visibility = Visibility.Collapsed;
        };

        var copyBtn = new Button
        {
            Content = "Copy to Clipboard",
            CornerRadius = new CornerRadius(4)
        };
        copyBtn.Click += (s, e) =>
        {
            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(snippetBox.Text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
            copyBtn.Content = "Copied!";
        };

        var actionPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        actionPanel.Children.Add(copyBtn);
        actionPanel.Children.Add(saveProfileBtn);

        var root = new StackPanel { Spacing = 12, MaxWidth = 540 };
        root.Children.Add(new TextBlock
        {
            Text = "Enable instant tab completion for tools installed on your workstation.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });
        root.Children.Add(radioPanel);
        root.Children.Add(snippetBox);
        root.Children.Add(statusLabel);
        root.Children.Add(actionPanel);

        var dialog = new ContentDialog
        {
            Title = "Terminal Shell Autocompletion",
            Content = root,
            CloseButtonText = "Done",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        await dialog.ShowAsync();
    }

    // ── Uninstall ───────────────────────────────────────────────────────

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ToolDefinition tool }) return;

        var dlFolder = DownloadService.DefaultDownloadsFolder;

        var (title, body, primary) = tool.Kind switch
        {
            ArtifactKind.Installer => (
                $"Uninstall {tool.Name}?",
                $"This opens the vendor uninstaller for {tool.Name}.",
                "Uninstall"),
            ArtifactKind.Archive => (
                $"Remove {tool.Name}?",
                $"This deletes the extracted files for {tool.Name} from your Tools folder.",
                "Remove"),
            ArtifactKind.Binary => (
                $"Remove {tool.Name}?",
                $"This deletes {tool.FileName} from Tools\\bin.",
                "Remove"),
            _ => (
                $"Delete {tool.Name}?",
                "This removes the installed files.",
                "Delete")
        };

        var dialog = new ContentDialog
        {
            Title = title,
            Content = body,
            PrimaryButtonText = primary,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        var result = UninstallService.Uninstall(tool, dlFolder, removeDownload: false);
        StatusText.Text = result.Message;

        if (result.Success)
        {
            tool.IsInstalled = false;
            // Re-scan to refresh the list
            await ScanInstalledToolsAsync();
        }
    }

    /// <summary>Uninstalls every selected tool, after one confirmation.</summary>
    private async void BulkUninstall_Click(object sender, RoutedEventArgs e)
    {
        var targets = _allInstalledTools.Where(t => t.IsSelected && t.SupportsUninstall).ToList();
        if (targets.Count == 0)
        {
            StatusText.Text = "None of the selected tools can be removed automatically.";
            return;
        }

        var names = string.Join("\n", targets.Take(8).Select(t => $"•  {t.Name}"));
        if (targets.Count > 8)
        {
            names += $"\n… and {targets.Count - 8} more";
        }

        var dialog = new ContentDialog
        {
            Title = targets.Count == 1 ? $"Uninstall {targets[0].Name}?" : $"Uninstall {targets.Count} tools?",
            Content = new TextBlock
            {
                Text = $"{names}\n\nVendor uninstallers are launched where the tool was installed by a setup package. Downloaded installers are kept in your Downloads folder.",
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 440
            },
            PrimaryButtonText = "Uninstall",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        var dlFolder = DownloadService.DefaultDownloadsFolder;
        var removed = 0;
        var failures = new List<string>();

        foreach (var tool in targets)
        {
            var result = UninstallService.Uninstall(tool, dlFolder, removeDownload: false);
            if (result.Success)
            {
                tool.IsInstalled = false;
                removed++;
            }
            else
            {
                failures.Add($"{tool.Name}: {result.Message}");
            }
        }

        StatusText.Text = failures.Count == 0
            ? $"Uninstalled {removed} tool(s)."
            : $"Uninstalled {removed} tool(s); {failures.Count} could not be removed.";

        await ScanInstalledToolsAsync();

        if (failures.Count > 0)
        {
            ToastService.Show("Some tools could not be removed", string.Join("  ·  ", failures.Take(3)));
        }
    }

    // ── Toolbar & export ────────────────────────────────────────────────

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _ = ScanInstalledToolsAsync();
    }

    void IRefreshablePage.RefreshView() => _ = ScanInstalledToolsAsync();

    private void OpenToolsFolder_Click(object sender, RoutedEventArgs e)
    {
        var toolsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DevOpsToolsInstaller", "Tools");
        if (!Directory.Exists(toolsFolder))
            Directory.CreateDirectory(toolsFolder);

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = toolsFolder,
            UseShellExecute = true
        });
    }

    private async void ExportReport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var reportPath = WorkstationReportService.DefaultReportPath;
            var catalogTools = _allInstalledTools.Count > 0
                ? _allInstalledTools
                : App.MainWindowInstance?.Tools.ToList() ?? new List<ToolDefinition>();
            var count = await WorkstationReportService.WriteReportAsync(catalogTools, reportPath);

            var openButton = new HyperlinkButton { Content = "Open containing folder" };
            openButton.Click += (_, _) =>
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{reportPath}\"",
                    UseShellExecute = true
                });
            };

            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = "Workstation report exported",
                Content = new StackPanel
                {
                    Spacing = 8,
                    MinWidth = 380,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"Report for {count} installed tool(s) written to:",
                            TextWrapping = TextWrapping.Wrap
                        },
                        new TextBox
                        {
                            Text = reportPath,
                            IsReadOnly = true,
                            TextWrapping = TextWrapping.Wrap
                        },
                        openButton
                    }
                },
                CloseButtonText = "Close",
                DefaultButton = ContentDialogButton.Close
            };

            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Report export failed: {ex.Message}";
        }
    }

    private async void ExportProfile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var filePath = Path.Combine(folder, $"workstation-profile-{DateTime.Now:yyyyMMdd-HHmm}.json");

            // Export writes every installed tool, so select them all for the
            // duration of the call — then put the user's own selection back, so
            // exporting never opens the bulk-action bar behind the dialog.
            var previouslySelected = _allInstalledTools.Where(t => t.IsSelected).ToHashSet();

            string json;
            _suppressAutoRefresh = true;
            try
            {
                foreach (var tool in _allInstalledTools)
                {
                    tool.IsSelected = true;
                }

                json = ProfileService.Export(_allInstalledTools, "Installed Tools Profile");
            }
            finally
            {
                foreach (var tool in _allInstalledTools)
                {
                    tool.IsSelected = previouslySelected.Contains(tool);
                }

                _suppressAutoRefresh = false;
                UpdateBulkBar();
            }

            await File.WriteAllTextAsync(filePath, json);

            var dialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = "Workstation Profile Exported",
                Content = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = $"Profile containing {_allInstalledTools.Count} tool(s) saved to:", TextWrapping = TextWrapping.Wrap },
                        new TextBox { Text = filePath, IsReadOnly = true, TextWrapping = TextWrapping.Wrap }
                    }
                },
                CloseButtonText = "OK",
                DefaultButton = ContentDialogButton.Close
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Profile export failed: {ex.Message}";
        }
    }

    private async void ExportBootstrap_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var filePath = Path.Combine(folder, $"devops-bootstrap-{DateTime.Now:yyyyMMdd-HHmm}.ps1");
            var script = BootstrapScriptService.Generate(_allInstalledTools, SettingsService.DownloadsFolder);
            await File.WriteAllTextAsync(filePath, script);

            var dialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = "PowerShell Setup Script Generated",
                Content = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"Self-contained unattended bootstrap script for {_allInstalledTools.Count} tool(s) generated at:",
                            TextWrapping = TextWrapping.Wrap
                        },
                        new TextBox { Text = filePath, IsReadOnly = true, TextWrapping = TextWrapping.Wrap },
                        new TextBlock
                        {
                            Text = "Execute this script in an elevated PowerShell session to provision any machine from scratch with zero prerequisites.",
                            FontSize = 12,
                            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                },
                CloseButtonText = "OK",
                DefaultButton = ContentDialogButton.Close
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Bootstrap generation failed: {ex.Message}";
        }
    }

    private async void ExportDevContainer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var filePath = Path.Combine(folder, $"devcontainer-workstation-{DateTime.Now:yyyyMMdd-HHmm}.zip");

            var toolsToExport = _allInstalledTools.Count > 0
                ? _allInstalledTools
                : App.MainWindowInstance?.Tools.ToList() ?? new List<ToolDefinition>();

            DevContainerGeneratorService.ExportDevContainerZip(toolsToExport, filePath, "DevOps Workstation");

            var dialog = new ContentDialog
            {
                XamlRoot = this.XamlRoot,
                Title = "DevContainer Exported",
                Content = new ScrollViewer
                {
                    Content = new StackPanel
                    {
                        Spacing = 12,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = $"DevContainer package containing {toolsToExport.Count} tool configuration(s) generated at:",
                                TextWrapping = TextWrapping.Wrap
                            },
                            new TextBox { Text = filePath, IsReadOnly = true, TextWrapping = TextWrapping.Wrap },
                            new TextBlock
                            {
                                Text = "Extract the .devcontainer folder into your project root and open in VS Code with the Dev Containers extension to develop in an isolated Linux container matching your DevOps toolchain.",
                                FontSize = 12,
                                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                                TextWrapping = TextWrapping.Wrap
                            }
                        }
                    }
                },
                CloseButtonText = "OK",
                DefaultButton = ContentDialogButton.Close
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"DevContainer export failed: {ex.Message}";
        }
    }

    private void GoToCatalog_Click(object sender, RoutedEventArgs e)
    {
        App.MainWindowInstance?.NavigateTo("Catalog");
    }
}
