using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using DevOpsToolsInstaller.Services;

namespace DevOpsToolsInstaller.ViewModels;

public sealed class LogEntryItemViewModel : ObservableObject
{
    public LogEntry Entry { get; }

    public DateTime Timestamp => Entry.Timestamp;
    public string TimeLabel => Entry.TimeLabel;
    public LogSeverity Severity => Entry.Severity;
    public string ToolName => Entry.ToolName;
    public string Message => Entry.Message;

    public string SeverityBadgeText => Severity switch
    {
        LogSeverity.Info => "INFO",
        LogSeverity.Success => "OK",
        LogSeverity.Warning => "WARN",
        LogSeverity.Error => "ERR",
        _ => "LOG"
    };

    public string SeverityGlyph => Severity switch
    {
        LogSeverity.Success => "\uE73E",  // Checkmark
        LogSeverity.Warning => "\uE7BA",  // Warning triangle
        LogSeverity.Error   => "\uE711",  // Error cross
        _                   => "\uE946"   // Info circle
    };

    public Brush SeverityBadgeBackground
    {
        get
        {
            var key = Severity switch
            {
                LogSeverity.Success => "SystemFillColorSuccessBackgroundBrush",
                LogSeverity.Warning => "SystemFillColorCautionBackgroundBrush",
                LogSeverity.Error   => "SystemFillColorCriticalBackgroundBrush",
                _                   => "SystemFillColorAttentionBackgroundBrush"
            };
            return Application.Current.Resources.TryGetValue(key, out var b) && b is Brush brush
                ? brush
                : new SolidColorBrush(Colors.Transparent);
        }
    }

    public Brush SeverityBadgeForeground
    {
        get
        {
            var key = Severity switch
            {
                LogSeverity.Success => "SystemFillColorSuccessBrush",
                LogSeverity.Warning => "SystemFillColorCautionBrush",
                LogSeverity.Error   => "SystemFillColorCriticalBrush",
                _                   => "SystemFillColorAttentionBrush"
            };
            return Application.Current.Resources.TryGetValue(key, out var b) && b is Brush brush
                ? brush
                : (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
        }
    }

    public Brush SeverityBorderBrush
    {
        get
        {
            var key = Severity switch
            {
                LogSeverity.Success => "SystemFillColorSuccessBrush",
                LogSeverity.Warning => "SystemFillColorCautionBrush",
                LogSeverity.Error   => "SystemFillColorCriticalBrush",
                _                   => "CardStrokeColorDefaultBrush"
            };
            return Application.Current.Resources.TryGetValue(key, out var b) && b is Brush brush
                ? brush
                : new SolidColorBrush(Colors.Transparent);
        }
    }

    public string AutomationName => $"{TimeLabel} [{SeverityBadgeText}] {ToolName}: {Message}";

    public LogEntryItemViewModel(LogEntry entry)
    {
        Entry = entry;
    }
}

public sealed class LogDrawerViewModel : ObservableObject
{
    private readonly List<LogEntryItemViewModel> _allEntries = new();
    public ObservableCollection<LogEntryItemViewModel> FilteredEntries { get; } = new();

    private string _filterLevel = "All";
    public string FilterLevel
    {
        get => _filterLevel;
        set
        {
            if (SetProperty(ref _filterLevel, value))
            {
                SettingsService.LogDrawerLevelFilter = value;
                ApplyFilter();
            }
        }
    }

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilter();
            }
        }
    }

    private bool _isAutoScrollEnabled = true;
    public bool IsAutoScrollEnabled
    {
        get => _isAutoScrollEnabled;
        set
        {
            if (SetProperty(ref _isAutoScrollEnabled, value))
            {
                SettingsService.LogDrawerAutoScroll = value;
                if (value && FilteredEntries.Count > 0)
                {
                    RequestScrollToBottom?.Invoke();
                }
            }
        }
    }

    private int _totalCount;
    public int TotalCount
    {
        get => _totalCount;
        set => SetProperty(ref _totalCount, value);
    }

    private int _filteredCount;
    public int FilteredCount
    {
        get => _filteredCount;
        set => SetProperty(ref _filteredCount, value);
    }

    private int _errorCount;
    public int ErrorCount
    {
        get => _errorCount;
        set => SetProperty(ref _errorCount, value);
    }

    private int _warningCount;
    public int WarningCount
    {
        get => _warningCount;
        set => SetProperty(ref _warningCount, value);
    }

    public string HeaderSummaryText => $"{TotalCount} events" +
        (ErrorCount > 0 ? $" ({ErrorCount} err)" : "") +
        (WarningCount > 0 ? $" ({WarningCount} warn)" : "");

    public event Action? RequestScrollToBottom;

    public LogDrawerViewModel()
    {
        _isAutoScrollEnabled = SettingsService.LogDrawerAutoScroll;
        _filterLevel = SettingsService.LogDrawerLevelFilter;
    }

    public void Initialize()
    {
        ActivityLogService.EntryAdded += OnEntryAdded;
        ActivityLogService.Cleared += OnCleared;

        // Populate existing entries in chronological order (oldest to newest)
        var existing = ActivityLogService.Entries.Reverse().ToList();
        _allEntries.Clear();
        foreach (var entry in existing)
        {
            _allEntries.Add(new LogEntryItemViewModel(entry));
        }

        UpdateCounts();
        ApplyFilter();
    }

    public void Teardown()
    {
        ActivityLogService.EntryAdded -= OnEntryAdded;
        ActivityLogService.Cleared -= OnCleared;
    }

    private void OnEntryAdded(LogEntry entry)
    {
        UiDispatcher.Run(() =>
        {
            var item = new LogEntryItemViewModel(entry);
            _allEntries.Add(item);
            UpdateCounts();

            if (MatchesFilter(item))
            {
                FilteredEntries.Add(item);
                FilteredCount = FilteredEntries.Count;

                if (IsAutoScrollEnabled)
                {
                    RequestScrollToBottom?.Invoke();
                }
            }
        });
    }

    private void OnCleared()
    {
        UiDispatcher.Run(() =>
        {
            _allEntries.Clear();
            FilteredEntries.Clear();
            UpdateCounts();
            FilteredCount = 0;
        });
    }

    private void UpdateCounts()
    {
        TotalCount = _allEntries.Count;
        ErrorCount = _allEntries.Count(e => e.Severity == LogSeverity.Error);
        WarningCount = _allEntries.Count(e => e.Severity == LogSeverity.Warning);
        OnPropertyChanged(nameof(HeaderSummaryText));
    }

    public void ApplyFilter()
    {
        FilteredEntries.Clear();
        foreach (var item in _allEntries)
        {
            if (MatchesFilter(item))
            {
                FilteredEntries.Add(item);
            }
        }
        FilteredCount = FilteredEntries.Count;

        if (IsAutoScrollEnabled && FilteredEntries.Count > 0)
        {
            RequestScrollToBottom?.Invoke();
        }
    }

    private bool MatchesFilter(LogEntryItemViewModel item)
    {
        // Level filter
        var levelMatch = FilterLevel switch
        {
            "Info" => item.Severity == LogSeverity.Info || item.Severity == LogSeverity.Success,
            "Warning" => item.Severity == LogSeverity.Warning,
            "Error" => item.Severity == LogSeverity.Error,
            _ => true
        };
        if (!levelMatch) return false;

        // Search text
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            return item.Message.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                   item.ToolName.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    public string GetClipboardText()
    {
        var sb = new StringBuilder();
        foreach (var item in FilteredEntries)
        {
            sb.AppendLine($"[{item.TimeLabel}] [{item.SeverityBadgeText}] [{item.ToolName}] {item.Message}");
        }
        return sb.ToString();
    }
}
