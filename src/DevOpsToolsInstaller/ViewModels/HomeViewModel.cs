using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using DevOpsToolsInstaller.Models;
using DevOpsToolsInstaller.Services;

namespace DevOpsToolsInstaller.ViewModels;

public sealed class ActiveDownloadItemViewModel : ObservableObject
{
    public ToolDefinition Tool { get; }

    public string Name => Tool.Name;
    public string Version => Tool.Version;
    public string IconGlyph => Tool.IconGlyph;
    public string LogoUrl => Tool.LogoUrl;
    public bool HasLogo => !string.IsNullOrWhiteSpace(LogoUrl);

    public double Progress => Tool.Progress;
    public string DownloadSpeed => string.IsNullOrWhiteSpace(Tool.DownloadSpeed) ? "Downloading…" : Tool.DownloadSpeed;
    public string ProgressPercentText => $"{Tool.Progress:F0}%";
    public string SpeedText => Tool.DownloadSpeed;

    public string AutomationLabel => $"{Tool.Name} {Tool.Version}, {Tool.Progress:F0} percent downloaded, {DownloadSpeed}";

    public ActiveDownloadItemViewModel(ToolDefinition tool)
    {
        Tool = tool;
        Tool.PropertyChanged += Tool_PropertyChanged;
    }

    private void Tool_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ToolDefinition.Progress))
        {
            OnPropertyChanged(nameof(Progress));
            OnPropertyChanged(nameof(ProgressPercentText));
            OnPropertyChanged(nameof(AutomationLabel));
        }
        else if (e.PropertyName == nameof(ToolDefinition.DownloadSpeed))
        {
            OnPropertyChanged(nameof(DownloadSpeed));
            OnPropertyChanged(nameof(SpeedText));
            OnPropertyChanged(nameof(AutomationLabel));
        }
    }

    public void Detach()
    {
        Tool.PropertyChanged -= Tool_PropertyChanged;
    }
}

public sealed class HomeStackItemViewModel : ObservableObject
{
    public ToolBundle Bundle { get; }

    public string Id => Bundle.Id;
    public string Name => Bundle.Name;
    public string Description => Bundle.Description;
    public string Glyph => Bundle.Glyph;
    public string Category => Bundle.Category;

    private int _installedCount;
    public int InstalledCount
    {
        get => _installedCount;
        set
        {
            if (SetProperty(ref _installedCount, value))
            {
                OnPropertyChanged(nameof(ProgressPercentage));
                OnPropertyChanged(nameof(IsComplete));
                OnPropertyChanged(nameof(IsPartiallyInstalled));
                OnPropertyChanged(nameof(IsNotStarted));
                OnPropertyChanged(nameof(InstalledProgressText));
                OnPropertyChanged(nameof(ActionText));
                OnPropertyChanged(nameof(ActionGlyph));
                OnPropertyChanged(nameof(AutomationLabel));
            }
        }
    }

    public int TotalCount => Bundle.Tools.Count;
    public double ProgressPercentage => TotalCount > 0 ? (double)InstalledCount / TotalCount * 100.0 : 0.0;

    public bool IsComplete => InstalledCount >= TotalCount;
    public bool IsPartiallyInstalled => InstalledCount > 0 && InstalledCount < TotalCount;
    public bool IsNotStarted => InstalledCount == 0;

    public string InstalledProgressText => $"{InstalledCount} of {TotalCount} installed";

    public string ActionText
    {
        get
        {
            if (IsComplete) return "Complete";
            if (InstalledCount > 0)
            {
                int missing = TotalCount - InstalledCount;
                if (InstalledCount <= 2 && TotalCount >= 5)
                    return "Continue setup";
                return $"Install {missing} missing";
            }
            return "Install stack";
        }
    }

    public string ActionGlyph => IsComplete ? "\uE73E" : "\uE896";

    public string EstimatedSizeText { get; }
    public IReadOnlyList<string> ToolChips { get; }

    public string AutomationLabel => $"{Name}, {InstalledCount} of {TotalCount} installed, action: {ActionText}";

    public HomeStackItemViewModel(ToolBundle bundle, int installedCount, IEnumerable<ToolDefinition> allTools)
    {
        Bundle = bundle;
        _installedCount = installedCount;

        var toolsById = allTools.ToDictionary(t => t.Id, t => t, StringComparer.OrdinalIgnoreCase);
        var chipNames = new List<string>();
        long totalEstimatedBytes = 0;

        foreach (var toolId in bundle.Tools)
        {
            if (toolsById.TryGetValue(toolId, out var tool))
            {
                chipNames.Add(tool.Name);
                totalEstimatedBytes += EstimateToolSizeBytes(tool);
            }
            else
            {
                chipNames.Add(toolId);
                totalEstimatedBytes += 45L * 1024 * 1024;
            }
        }

        ToolChips = chipNames;
        int mb = (int)Math.Max(10, totalEstimatedBytes / (1024 * 1024));
        EstimatedSizeText = $"{TotalCount} tools, about {mb} MB";
    }

    private static long EstimateToolSizeBytes(ToolDefinition tool)
    {
        if (tool.Kind == ArtifactKind.Installer)
        {
            if (tool.Id.Contains("desktop", StringComparison.OrdinalIgnoreCase))
                return 550L * 1024 * 1024;
            if (tool.Id.Contains("cli", StringComparison.OrdinalIgnoreCase))
                return 120L * 1024 * 1024;
            return 80L * 1024 * 1024;
        }
        if (tool.Kind == ArtifactKind.Archive)
            return 50L * 1024 * 1024;
        return 35L * 1024 * 1024;
    }
}

public sealed class HomeUpdateItemViewModel : ObservableObject
{
    public ToolDefinition Tool { get; }

    public string Name => Tool.Name;
    public string CurrentVersion { get; }
    public string NewVersion { get; }
    public string VersionTransitionText { get; }

    public string IconGlyph => Tool.IconGlyph;
    public string LogoUrl => Tool.LogoUrl;
    public bool HasLogo => !string.IsNullOrWhiteSpace(LogoUrl);

    public string AutomationLabel => $"Update {Name} from {CurrentVersion} to {NewVersion}";

    public HomeUpdateItemViewModel(ToolDefinition tool, string currentVer, string newVer, string transitionText)
    {
        Tool = tool;
        CurrentVersion = currentVer;
        NewVersion = newVer;
        VersionTransitionText = transitionText;
    }
}

public sealed class HomePopularToolItemViewModel : ObservableObject
{
    public ToolDefinition Tool { get; }

    public string Id => Tool.Id;
    public string Name => Tool.Name;
    public string Category => Tool.Category;
    public string Description => Tool.Description;
    public string Version => Tool.Version;

    public string IconGlyph => Tool.IconGlyph;
    public string LogoUrl => Tool.LogoUrl;
    public bool HasLogo => !string.IsNullOrWhiteSpace(LogoUrl);

    public bool IsInstalled => Tool.IsInstalled || Tool.Status == ToolStatus.Downloaded;
    public string StatusText => IsInstalled ? "Installed" : "Available";

    public string AutomationLabel => $"{Name}, {Category}, {(IsInstalled ? "Installed" : "Not installed, click to install")}";
    public string InstallButtonAutomationLabel => $"Install {Name}";

    public HomePopularToolItemViewModel(ToolDefinition tool)
    {
        Tool = tool;
        Tool.PropertyChanged += Tool_PropertyChanged;
    }

    private void Tool_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ToolDefinition.IsInstalled) ||
            e.PropertyName == nameof(ToolDefinition.Status))
        {
            OnPropertyChanged(nameof(IsInstalled));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(AutomationLabel));
        }
    }
}

public sealed partial class HomeViewModel : ObservableObject
{
    private readonly IPathHealthService _pathHealthService;
    private readonly IToolUpdateService _toolUpdateService;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _hasInstalledTools;

    [ObservableProperty]
    private int _installedCount;

    [ObservableProperty]
    private int _updateCount;

    [ObservableProperty]
    private int _activeDownloadsCount;

    [ObservableProperty]
    private bool _hasActiveDownloads;

    [ObservableProperty]
    private string? _nextInQueueText;

    [ObservableProperty]
    private bool _hasNextInQueue;

    [ObservableProperty]
    private string _pathStatus = "Checking…";

    [ObservableProperty]
    private bool _isPathHealthy = true;

    [ObservableProperty]
    private string _pathSummary = string.Empty;

    [ObservableProperty]
    private string _pathBinFolder = string.Empty;

    [ObservableProperty]
    private string _pathFixCommand = string.Empty;

    [ObservableProperty]
    private string _dynamicSubtitle = "Loading workstation overview…";

    [ObservableProperty]
    private bool _hasUpdates;

    [ObservableProperty]
    private bool _canUpdateAll;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    public string InstalledMetricAutomationName => $"Installed tools: {InstalledCount}";
    public string UpdatesMetricAutomationName => $"Tool updates available: {UpdateCount}";
    public string DownloadsMetricAutomationName => $"Active downloads: {ActiveDownloadsCount}";
    public string PathMetricAutomationName => $"PATH health status: {PathStatus}";

    partial void OnInstalledCountChanged(int value) => OnPropertyChanged(nameof(InstalledMetricAutomationName));
    partial void OnUpdateCountChanged(int value) => OnPropertyChanged(nameof(UpdatesMetricAutomationName));
    partial void OnActiveDownloadsCountChanged(int value) => OnPropertyChanged(nameof(DownloadsMetricAutomationName));
    partial void OnPathStatusChanged(string value) => OnPropertyChanged(nameof(PathMetricAutomationName));

    public ObservableCollection<ActiveDownloadItemViewModel> ActiveDownloads { get; } = new();
    public ObservableCollection<HomeStackItemViewModel> Stacks { get; } = new();
    public ObservableCollection<HomeUpdateItemViewModel> Updates { get; } = new();
    public ObservableCollection<HomePopularToolItemViewModel> PopularTools { get; } = new();
    public ObservableCollection<string> SearchSuggestions { get; } = new();

    private readonly List<ToolDefinition> _installedTools = new();
    private ObservableCollection<ToolDefinition>? _boundDownloadQueue;

    public HomeViewModel(
        IPathHealthService? pathHealthService = null,
        IToolUpdateService? toolUpdateService = null)
    {
        _pathHealthService = pathHealthService ?? new PathHealthService();
        _toolUpdateService = toolUpdateService ?? new ToolUpdateService();
    }

    public void BindDownloadQueue(ObservableCollection<ToolDefinition> downloadQueue)
    {
        if (_boundDownloadQueue == downloadQueue) return;

        if (_boundDownloadQueue != null)
        {
            _boundDownloadQueue.CollectionChanged -= DownloadQueue_CollectionChanged;
        }

        _boundDownloadQueue = downloadQueue;
        _boundDownloadQueue.CollectionChanged += DownloadQueue_CollectionChanged;

        SyncActiveDownloads();
    }

    private void DownloadQueue_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SyncActiveDownloads();
    }

    public void SyncActiveDownloads()
    {
        if (_boundDownloadQueue == null)
        {
            ActiveDownloads.Clear();
            HasActiveDownloads = false;
            ActiveDownloadsCount = 0;
            NextInQueueText = null;
            HasNextInQueue = false;
            return;
        }

        var downloading = _boundDownloadQueue
            .Where(t => t.Status == ToolStatus.Downloading)
            .ToList();

        var queued = _boundDownloadQueue
            .Where(t => t.Status == ToolStatus.NotDownloaded)
            .ToList();

        // Update active downloads list
        var existingTools = ActiveDownloads.Select(a => a.Tool).ToHashSet();
        var toRemove = ActiveDownloads.Where(a => !downloading.Contains(a.Tool)).ToList();
        foreach (var item in toRemove)
        {
            item.Detach();
            ActiveDownloads.Remove(item);
        }

        foreach (var tool in downloading)
        {
            if (!existingTools.Contains(tool))
            {
                ActiveDownloads.Add(new ActiveDownloadItemViewModel(tool));
            }
        }

        ActiveDownloadsCount = downloading.Count;
        HasActiveDownloads = downloading.Count > 0;

        if (queued.Count > 0)
        {
            var names = queued.Take(3).Select(t => t.Name);
            var remaining = queued.Count > 3 ? $" (+{queued.Count - 3} more)" : "";
            NextInQueueText = $"Next in queue: {string.Join(", ", names)}{remaining}";
            HasNextInQueue = true;
        }
        else
        {
            NextInQueueText = null;
            HasNextInQueue = false;
        }
    }

    public async Task LoadDashboardAsync(MainWindow mw, CancellationToken ct = default)
    {
        IsLoading = true;
        HasError = false;
        ErrorMessage = null;

        try
        {
            await mw.EnsureCatalogLoadedAsync();

            BindDownloadQueue(mw.DownloadQueue);

            // 1. Scan installed tools
            var dlFolder = DownloadService.DefaultDownloadsFolder;
            var detected = await Task.Run(() =>
            {
                var list = new List<ToolDefinition>();
                foreach (var tool in mw.Tools)
                {
                    if (UninstallService.IsInstalled(tool, dlFolder) || tool.Status == ToolStatus.Downloaded)
                    {
                        tool.IsInstalled = true;
                        if (tool.Kind == ArtifactKind.Installer && string.IsNullOrWhiteSpace(tool.DetectedVersion))
                        {
                            var v = UninstallService.GetInstalledVersion(tool);
                            if (!string.IsNullOrWhiteSpace(v)) tool.DetectedVersion = v;
                        }
                        list.Add(tool);
                    }
                }
                return list;
            }, ct);

            _installedTools.Clear();
            _installedTools.AddRange(detected);
            InstalledCount = _installedTools.Count;
            HasInstalledTools = InstalledCount > 0;

            // 2. PATH Health check
            var pathReport = _pathHealthService.CheckPathHealth();
            PathStatus = pathReport.StatusBadge;
            IsPathHealthy = pathReport.IsHealthy;
            PathSummary = pathReport.Summary;
            PathBinFolder = pathReport.BinFolder;
            PathFixCommand = pathReport.SuggestedFixCommand;

            // 3. Tool Updates check
            var updates = await _toolUpdateService.CheckForUpdatesAsync(_installedTools, ct);
            Updates.Clear();
            foreach (var upd in updates)
            {
                Updates.Add(new HomeUpdateItemViewModel(
                    upd.Tool, upd.CurrentVersion, upd.NewVersion, upd.VersionTransitionText));
            }
            UpdateCount = Updates.Count;
            HasUpdates = UpdateCount > 0;
            CanUpdateAll = UpdateCount >= 2;

            // Dynamic Subtitle
            UpdateDynamicSubtitle();

            // 4. Stacks & Ordering
            BuildStacks(mw);

            // 5. Popular Tools
            BuildPopularTools(mw);

            IsLoading = false;
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"Unable to load workstation dashboard: {ex.Message}";
            IsLoading = false;
        }
    }

    private void UpdateDynamicSubtitle()
    {
        if (HasInstalledTools)
        {
            string pathDesc = IsPathHealthy ? "PATH is healthy." : "PATH needs attention.";
            if (UpdateCount > 0)
            {
                string s = UpdateCount == 1 ? "" : "s";
                DynamicSubtitle = $"{UpdateCount} update{s} available. {pathDesc}";
            }
            else
            {
                DynamicSubtitle = $"All {InstalledCount} tools are up to date. {pathDesc}";
            }
        }
        else
        {
            DynamicSubtitle = "Select a curated stack or search the catalog to configure your workstation.";
        }
    }

    private void BuildStacks(MainWindow mw)
    {
        var toolsById = mw.Tools.ToDictionary(t => t.Id, t => t, StringComparer.OrdinalIgnoreCase);

        var stackList = new List<HomeStackItemViewModel>();
        foreach (var bundle in mw.Bundles)
        {
            int installedInBundle = 0;
            foreach (var toolId in bundle.Tools)
            {
                if (toolsById.TryGetValue(toolId, out var tool))
                {
                    if (tool.IsInstalled || tool.Status == ToolStatus.Downloaded)
                    {
                        installedInBundle++;
                    }
                }
            }

            stackList.Add(new HomeStackItemViewModel(bundle, installedInBundle, mw.Tools));
        }

        // Order stacks by relevance: partially installed first, then not started, then complete.
        var ordered = stackList
            .OrderBy(s => s.IsPartiallyInstalled ? 0 : (s.IsNotStarted ? 1 : 2))
            .ThenByDescending(s => s.InstalledCount)
            .ToList();

        Stacks.Clear();
        foreach (var item in ordered)
        {
            Stacks.Add(item);
        }
    }

    private void BuildPopularTools(MainWindow mw)
    {
        // Defined popular & essential tools
        var targetIds = new[]
        {
            "docker-desktop",
            "kubectl",
            "terraform",
            "git",
            "azure-cli",
            "awscli",
            "helm",
            "trivy"
        };

        var toolsById = mw.Tools.ToDictionary(t => t.Id, t => t, StringComparer.OrdinalIgnoreCase);

        PopularTools.Clear();
        foreach (var id in targetIds)
        {
            if (toolsById.TryGetValue(id, out var tool))
            {
                PopularTools.Add(new HomePopularToolItemViewModel(tool));
            }
        }
    }

    public void UpdateSearchSuggestions(string query, MainWindow mw)
    {
        SearchSuggestions.Clear();
        if (string.IsNullOrWhiteSpace(query)) return;

        var matches = mw.Tools
            .Where(t => t.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        t.Category.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(5)
            .Select(t => t.Name)
            .ToList();

        foreach (var m in matches)
        {
            SearchSuggestions.Add(m);
        }
    }

    public async Task InstallToolAsync(ToolDefinition tool, MainWindow mw)
    {
        tool.IsSelected = true;
        if (!mw.DownloadQueue.Contains(tool))
        {
            mw.DownloadQueue.Add(tool);
        }

        SyncActiveDownloads();

        _ = Task.Run(async () =>
        {
            try
            {
                var dlFolder = DownloadService.DefaultDownloadsFolder;
                await mw.DownloadSvc.DownloadAsync(tool, dlFolder);
            }
            catch { }
            finally
            {
                _ = mw.DispatcherQueue.TryEnqueue(() =>
                {
                    SyncActiveDownloads();
                    UpdateDynamicSubtitle();
                });
            }
        });
    }

    public async Task UpdateToolAsync(ToolDefinition tool, MainWindow mw)
    {
        await InstallToolAsync(tool, mw);
    }

    public async Task UpdateAllAsync(MainWindow mw)
    {
        var toolsToUpdate = Updates.Select(u => u.Tool).ToList();
        foreach (var tool in toolsToUpdate)
        {
            tool.IsSelected = true;
            if (!mw.DownloadQueue.Contains(tool))
            {
                mw.DownloadQueue.Add(tool);
            }
        }

        SyncActiveDownloads();

        _ = Task.Run(async () =>
        {
            var dlFolder = DownloadService.DefaultDownloadsFolder;
            await mw.DownloadSvc.DownloadBatchAsync(toolsToUpdate, dlFolder);

            _ = mw.DispatcherQueue.TryEnqueue(() =>
            {
                SyncActiveDownloads();
                Updates.Clear();
                UpdateCount = 0;
                HasUpdates = false;
                CanUpdateAll = false;
                UpdateDynamicSubtitle();
            });
        });
    }

    public void InstallStack(HomeStackItemViewModel stack, MainWindow mw)
    {
        var toolsById = mw.Tools.ToDictionary(t => t.Id, t => t, StringComparer.OrdinalIgnoreCase);
        var missingTools = new List<ToolDefinition>();

        foreach (var toolId in stack.Bundle.Tools)
        {
            if (toolsById.TryGetValue(toolId, out var tool))
            {
                if (!tool.IsInstalled && tool.Status != ToolStatus.Downloaded)
                {
                    missingTools.Add(tool);
                }
            }
        }

        if (missingTools.Count == 0)
        {
            // Already complete, navigate to Catalog with bundle
            mw.NavigateToCatalogWithBundle(stack.Id);
            return;
        }

        foreach (var tool in missingTools)
        {
            tool.IsSelected = true;
            if (!mw.DownloadQueue.Contains(tool))
            {
                mw.DownloadQueue.Add(tool);
            }
        }

        SyncActiveDownloads();

        _ = Task.Run(async () =>
        {
            var dlFolder = DownloadService.DefaultDownloadsFolder;
            await mw.DownloadSvc.DownloadBatchAsync(missingTools, dlFolder);
            _ = mw.DispatcherQueue.TryEnqueue(SyncActiveDownloads);
        });

        mw.NavigateTo("Downloads");
    }
}
