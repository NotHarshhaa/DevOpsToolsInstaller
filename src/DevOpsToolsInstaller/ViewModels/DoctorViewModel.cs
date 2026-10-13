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

    public Microsoft.UI.Xaml.Media.Brush StatusForegroundBrush
    {
        get
        {
            var key = Status switch
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
    }

    public Microsoft.UI.Xaml.Media.Brush StatusBackgroundBrush
    {
        get
        {
            var key = Status switch
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

public sealed class DoctorGroupViewModel
{
    public string Category { get; set; } = string.Empty;
    public string CategoryIcon { get; set; } = "\uE9D9";
    public ObservableCollection<DoctorCheckItemViewModel> Items { get; set; } = new();
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

            SummaryText = $"{PassedCount} passed, {WarningCount} warning{(WarningCount == 1 ? "" : "s")}, {FailedCount} failed";

            GroupedChecks.Clear();
            foreach (var group in report.Checks.GroupBy(c => c.Category))
            {
                var icon = GetCategoryIcon(group.Key);
                var groupVm = new DoctorGroupViewModel
                {
                    Category = group.Key,
                    CategoryIcon = icon,
                    Items = new ObservableCollection<DoctorCheckItemViewModel>(
                        group.Select(item => new DoctorCheckItemViewModel(item)))
                };
                GroupedChecks.Add(groupVm);
            }
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

    public string GetMarkdownReport()
    {
        return LastReport?.ToMarkdown() ?? "# Workstation Doctor Report\nNo diagnostic data available.";
    }

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
