using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using DevOpsToolsInstaller.Models;
using DevOpsToolsInstaller.Services;

namespace DevOpsToolsInstaller.ViewModels;

public sealed class DoctorCheckItemViewModel : ObservableObject
{
    public DoctorCheckResult Result { get; }

    public string Id => Result.Id;
    public string Category => Result.Category;
    public string Title => Result.Title;
    public string Explanation => Result.Explanation;
    public string Details => Result.Details;
    public bool HasDetails => !string.IsNullOrWhiteSpace(Details);
    public DoctorStatus Status => Result.Status;

    public string StatusBadgeText => Status switch
    {
        DoctorStatus.Passed => "Passed",
        DoctorStatus.Warning => "Warning",
        DoctorStatus.Failed => "Failed",
        _ => "Info"
    };

    public string StatusGlyph => Status switch
    {
        DoctorStatus.Passed => "\uE73E",   // Checkmark
        DoctorStatus.Warning => "\uE7BA",  // Warning triangle
        DoctorStatus.Failed => "\uE711",   // Error cross
        _ => "\uE946"
    };

    public bool IsPassed => Status == DoctorStatus.Passed;
    public bool IsWarning => Status == DoctorStatus.Warning;
    public bool IsFailed => Status == DoctorStatus.Failed;

    public Microsoft.UI.Xaml.Media.Brush StatusForegroundBrush => ForegroundFor(Status);

    public Microsoft.UI.Xaml.Media.Brush StatusBackgroundBrush => BackgroundFor(Status);

    /// <summary>Sort weight: failures rank above warnings, which rank above passes.</summary>
    public static int RankOf(DoctorStatus status) => status switch
    {
        DoctorStatus.Failed => 2,
        DoctorStatus.Warning => 1,
        _ => 0
    };

    /// <summary>Theme-aware accent colour for a status badge or icon.</summary>
    public static Microsoft.UI.Xaml.Media.Brush ForegroundFor(DoctorStatus status)
    {
        var key = status switch
        {
            DoctorStatus.Passed => "SystemFillColorSuccessBrush",
            DoctorStatus.Warning => "SystemFillColorCautionBrush",
            DoctorStatus.Failed => "SystemFillColorCriticalBrush",
            _ => "TextFillColorSecondaryBrush"
        };
        return Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue(key, out var b) && b is Microsoft.UI.Xaml.Media.Brush brush
            ? brush
            : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    /// <summary>Theme-aware background for a status badge.</summary>
    public static Microsoft.UI.Xaml.Media.Brush BackgroundFor(DoctorStatus status)
    {
        var key = status switch
        {
            DoctorStatus.Passed => "SystemFillColorSuccessBackgroundBrush",
            DoctorStatus.Warning => "SystemFillColorCautionBackgroundBrush",
            DoctorStatus.Failed => "SystemFillColorCriticalBackgroundBrush",
            _ => "CardBackgroundFillColorDefaultBrush"
        };
        return Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue(key, out var b) && b is Microsoft.UI.Xaml.Media.Brush brush
            ? brush
            : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    public bool HasFixAction => Result.HasFixAction;
    public string FixButtonText => Result.FixButtonText;
    public string FixAutomationName => $"Fix {Title}";
    public bool HasLearnMore => Result.HasLearnMore;
    public string LearnMoreTitle => Result.LearnMoreTitle;
    public string LearnMoreAutomationName => $"Learn more about {Title}";

    public string AutomationDescription => $"{Title}, status: {StatusBadgeText}. {Explanation}";

    public DoctorCheckItemViewModel(DoctorCheckResult result)
    {
        Result = result;
    }
}

public sealed partial class DoctorGroupViewModel : ObservableObject
{
    public string Category { get; set; } = string.Empty;
    public string CategoryIcon { get; set; } = "\uE9D9";
    public ObservableCollection<DoctorCheckItemViewModel> Items { get; set; } = new();

    /// <summary>Worst status found in the group — drives sort order and the header accent.</summary>
    public DoctorStatus Severity => Items.Count == 0
        ? DoctorStatus.Passed
        : Items.Select(i => i.Status).OrderByDescending(DoctorCheckItemViewModel.RankOf).First();

    public bool HasProblems => Severity != DoctorStatus.Passed;

    /// <summary>Number of checks in the group that did not pass.</summary>
    public int ProblemCount => Items.Count(i => i.Status != DoctorStatus.Passed);

    private bool _isExpanded = true;
    /// <summary>Whether the group is expanded. Problem groups open by default.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public string CountBadgeText => Items.Count == 1 ? "1 check" : $"{Items.Count} checks";

    public string StatusText => Severity switch
    {
        DoctorStatus.Failed => "Failed",
        DoctorStatus.Warning => "Warning",
        _ => "Passed"
    };

    public string StatusGlyph => Severity switch
    {
        DoctorStatus.Passed => "\uE73E",
        DoctorStatus.Warning => "\uE7BA",
        DoctorStatus.Failed => "\uE711",
        _ => "\uE946"
    };

    public Microsoft.UI.Xaml.Media.Brush StatusForegroundBrush => DoctorCheckItemViewModel.ForegroundFor(Severity);
    public Microsoft.UI.Xaml.Media.Brush StatusBackgroundBrush => DoctorCheckItemViewModel.BackgroundFor(Severity);

    public string AutomationDescription => HasProblems
        ? $"{Category}: {ProblemCount} of {Items.Count} checks need attention."
        : $"{Category}: all {Items.Count} checks passed.";
}

public sealed class DoctorViewModel : ObservableObject
{
    private bool _isScanning;
    public bool IsScanning
    {
        get => _isScanning;
        set => SetProperty(ref _isScanning, value);
    }

    private string _summaryText = "Running workstation checks…";
    public string SummaryText
    {
        get => _summaryText;
        set => SetProperty(ref _summaryText, value);
    }

    private int _passedCount;
    public int PassedCount
    {
        get => _passedCount;
        set
        {
            if (SetProperty(ref _passedCount, value))
            {
                OnPropertyChanged(nameof(PassedAutomationName));
            }
        }
    }
    public string PassedAutomationName => $"{PassedCount} checks passed";

    private int _warningCount;
    public int WarningCount
    {
        get => _warningCount;
        set
        {
            if (SetProperty(ref _warningCount, value))
            {
                OnPropertyChanged(nameof(WarningAutomationName));
            }
        }
    }
    public string WarningAutomationName => $"{WarningCount} warnings";

    private int _failedCount;
    public int FailedCount
    {
        get => _failedCount;
        set
        {
            if (SetProperty(ref _failedCount, value))
            {
                OnPropertyChanged(nameof(FailedAutomationName));
            }
        }
    }
    public string FailedAutomationName => $"{FailedCount} failed";

    private int _totalCount;
    public int TotalCount
    {
        get => _totalCount;
        set => SetProperty(ref _totalCount, value);
    }

    private int _healthScore;
    /// <summary>Weighted 0-100 workstation health score for the latest scan.</summary>
    public int HealthScore
    {
        get => _healthScore;
        set => SetProperty(ref _healthScore, value);
    }

    public string HealthScoreText => TotalCount == 0 ? "—" : $"{HealthScore}";

    public string HealthHeadline => TotalCount == 0
        ? "Not scanned"
        : FailedCount > 0
            ? "Needs attention"
            : WarningCount > 0
                ? "Minor issues"
                : "Healthy";

    public string HealthScoreAutomationName =>
        TotalCount == 0
            ? "Workstation health score not yet scanned."
            : $"Workstation health score {HealthScore} out of 100. {HealthHeadline}.";

    public Microsoft.UI.Xaml.Media.Brush HealthScoreForegroundBrush =>
        TotalCount == 0 ? DoctorCheckItemViewModel.ForegroundFor(DoctorStatus.Passed)
        : FailedCount > 0 ? DoctorCheckItemViewModel.ForegroundFor(DoctorStatus.Failed)
        : WarningCount > 0 ? DoctorCheckItemViewModel.ForegroundFor(DoctorStatus.Warning)
        : DoctorCheckItemViewModel.ForegroundFor(DoctorStatus.Passed);

    private DoctorReport? _lastReport;
    public DoctorReport? LastReport
    {
        get => _lastReport;
        set => SetProperty(ref _lastReport, value);
    }

    public ObservableCollection<DoctorGroupViewModel> GroupedChecks { get; } = new();

    public async Task RunScanAsync(IReadOnlyList<ToolDefinition>? catalogTools = null, CancellationToken ct = default)
    {
        IsScanning = true;
        SummaryText = "Scanning environment, PATH, and CLI tools…";

        try
        {
            var report = await DoctorService.RunDiagnosticsAsync(catalogTools, ct);
            LastReport = report;

            PassedCount = report.PassedCount;
            WarningCount = report.WarningCount;
            FailedCount = report.FailedCount;
            TotalCount = report.TotalCount;
            HealthScore = report.HealthScore;

            SummaryText = $"{PassedCount} passed, {WarningCount} warning{(WarningCount == 1 ? "" : "s")}, {FailedCount} failed";

            GroupedChecks.Clear();

            // Order each group's checks by severity (failed first) and order the
            // groups themselves so the ones needing attention surface at the top.
            var groups = report.Checks
                .GroupBy(c => c.Category)
                .Select(group => new DoctorGroupViewModel
                {
                    Category = group.Key,
                    CategoryIcon = GetCategoryIcon(group.Key),
                    Items = new ObservableCollection<DoctorCheckItemViewModel>(
                        group
                            .OrderByDescending(c => DoctorCheckItemViewModel.RankOf(c.Status))
                            .ThenBy(c => c.Title, StringComparer.CurrentCultureIgnoreCase)
                            .Select(c => new DoctorCheckItemViewModel(c)))
                })
                .OrderByDescending(g => DoctorCheckItemViewModel.RankOf(g.Severity))
                .ThenBy(g => g.Category, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            foreach (var group in groups)
            {
                // Expand problem groups so failures are visible immediately; keep
                // fully-passing groups collapsed to reduce scanning effort.
                group.IsExpanded = group.Severity != DoctorStatus.Passed;
                GroupedChecks.Add(group);
            }

            OnPropertyChanged(nameof(HealthScoreText));
            OnPropertyChanged(nameof(HealthHeadline));
            OnPropertyChanged(nameof(HealthScoreAutomationName));
            OnPropertyChanged(nameof(HealthScoreForegroundBrush));
        }
        catch (OperationCanceledException)
        {
            SummaryText = "Check scan cancelled.";
        }
        catch (Exception ex)
        {
            SummaryText = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    public string GetMarkdownReport(bool redactSensitiveInfo = false)
    {
        var markdown = LastReport?.ToMarkdown() ?? "# Workstation Doctor Report\nNo diagnostic data available.";
        return redactSensitiveInfo ? RedactSensitiveInfo(markdown) : markdown;
    }

    /// <summary>
    /// Replaces the current user profile path, username and machine name with
    /// environment-variable placeholders (%USERPROFILE%, %USERNAME%,
    /// %COMPUTERNAME%) so a diagnostic report can be shared safely.
    /// </summary>
    public static string RedactSensitiveInfo(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile))
        {
            var winPath = profile.TrimEnd('\\', '/');
            text = ReplaceOrdinalIgnoreCase(text, winPath, "%USERPROFILE%");
            text = ReplaceOrdinalIgnoreCase(text, winPath.Replace('\\', '/'), "%USERPROFILE%");
        }

        var userName = Environment.UserName;
        if (!string.IsNullOrWhiteSpace(userName))
        {
            text = ReplaceOrdinalIgnoreCase(text, $@"C:\Users\{userName}", "%USERPROFILE%");
            text = ReplaceOrdinalIgnoreCase(text, $@"\Users\{userName}", @"\Users\%USERNAME%");
        }

        var machineName = Environment.MachineName;
        if (!string.IsNullOrWhiteSpace(machineName))
        {
            text = ReplaceOrdinalIgnoreCase(text, machineName, "%COMPUTERNAME%");
        }

        return text;
    }

    private static string ReplaceOrdinalIgnoreCase(string source, string oldValue, string newValue)
        => string.IsNullOrEmpty(oldValue) ? source : source.Replace(oldValue, newValue, StringComparison.OrdinalIgnoreCase);

    private static string GetCategoryIcon(string category) => category switch
    {
        "Environment & PATH Health" => "\uE756",
        "Prerequisites & System Capabilities" => "\uE71D",
        "CLI Binary Conflicts & Shadowing" => "\uE895",
        "Version Freshness & Outdated Tools" => "\uE777",
        "PATH Integration & Discovery" => "\uE8B7",
        _ => "\uE9D9"
    };
}
