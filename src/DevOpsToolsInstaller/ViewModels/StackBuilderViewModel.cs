using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using DevOpsToolsInstaller.Models;
using DevOpsToolsInstaller.Services;

namespace DevOpsToolsInstaller.ViewModels;

public sealed class StackBuilderViewModel : ObservableObject
{
    private readonly List<ToolDefinition> _allCatalogTools = new();

    public ObservableCollection<ToolDefinition> FilteredAvailableTools { get; } = new();

    public ObservableCollection<ToolDefinition> StackTools { get; } = new();

    private string _stackId = string.Empty;
    public string StackId
    {
        get => _stackId;
        set => SetProperty(ref _stackId, value);
    }

    private string _searchQuery = string.Empty;
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                ApplyFilter();
            }
        }
    }

    public int FilteredAvailableCount => FilteredAvailableTools.Count;

    private string _stackName = "My Custom Stack";
    public string StackName
    {
        get => _stackName;
        set
        {
            if (SetProperty(ref _stackName, value))
            {
                OnPropertyChanged(nameof(CanInstall));
            }
        }
    }

    private string _stackDescription = "Custom DevOps toolset tailored for our team workflow.";
    public string StackDescription
    {
        get => _stackDescription;
        set => SetProperty(ref _stackDescription, value);
    }

    public int TotalToolsCount => StackTools.Count;
    public string ToolsCountBadgeText => $"{TotalToolsCount} Tool{(TotalToolsCount == 1 ? "" : "s")}";
    public bool HasTools => TotalToolsCount > 0;
    public bool IsEmpty => TotalToolsCount == 0;
    public bool CanInstall => HasTools;

    private long _estimatedSizeBytes;
    public long EstimatedSizeBytes
    {
        get => _estimatedSizeBytes;
        private set
        {
            if (SetProperty(ref _estimatedSizeBytes, value))
            {
                OnPropertyChanged(nameof(EstimatedSizeFormatted));
                OnPropertyChanged(nameof(SummaryMetricsText));
            }
        }
    }

    public string EstimatedSizeFormatted => CustomStacksService.FormatBytes(EstimatedSizeBytes);

    public string SummaryMetricsText => $"{ToolsCountBadgeText} • Est. Download: {EstimatedSizeFormatted}";

    public StackBuilderViewModel()
    {
        StackTools.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(TotalToolsCount));
            OnPropertyChanged(nameof(ToolsCountBadgeText));
            OnPropertyChanged(nameof(HasTools));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(CanInstall));
            RecalculateSize();
        };
    }

    public void Initialize(IReadOnlyList<ToolDefinition> catalogTools)
    {
        _allCatalogTools.Clear();
        _allCatalogTools.AddRange(catalogTools);
        ApplyFilter();
    }

    public void ApplyFilter()
    {
        var query = SearchQuery?.Trim() ?? string.Empty;
        var filtered = _allCatalogTools.Where(t =>
        {
            if (string.IsNullOrEmpty(query)) return true;
            return t.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                   t.Category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                   t.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                   t.Id.Contains(query, StringComparison.OrdinalIgnoreCase);
        }).ToList();

        FilteredAvailableTools.Clear();
        foreach (var tool in filtered)
        {
            FilteredAvailableTools.Add(tool);
        }
        OnPropertyChanged(nameof(FilteredAvailableCount));
    }

    public bool IsInStack(string toolId)
    {
        return StackTools.Any(t => string.Equals(t.Id, toolId, StringComparison.OrdinalIgnoreCase));
    }

    public bool AddTool(ToolDefinition tool)
    {
        if (IsInStack(tool.Id))
            return false;

        StackTools.Add(tool);
        return true;
    }

    public bool AddToolById(string toolId)
    {
        if (IsInStack(toolId)) return false;

        var tool = _allCatalogTools.FirstOrDefault(t => string.Equals(t.Id, toolId, StringComparison.OrdinalIgnoreCase));
        if (tool != null)
        {
            StackTools.Add(tool);
            return true;
        }
        return false;
    }

    public void RemoveTool(ToolDefinition tool)
    {
        StackTools.Remove(tool);
    }

    public void MoveUp(ToolDefinition tool)
    {
        int index = StackTools.IndexOf(tool);
        if (index > 0)
        {
            StackTools.Move(index, index - 1);
        }
    }

    public void MoveDown(ToolDefinition tool)
    {
        int index = StackTools.IndexOf(tool);
        if (index >= 0 && index < StackTools.Count - 1)
        {
            StackTools.Move(index, index + 1);
        }
    }

    public void Clear()
    {
        StackId = string.Empty;
        StackTools.Clear();
        StackName = "My Custom Stack";
        StackDescription = string.Empty;
    }

    public void LoadBundle(ToolBundle bundle)
    {
        StackId = bundle.Id;
        StackTools.Clear();
        StackName = bundle.Name;
        StackDescription = bundle.Description;

        var lookup = _allCatalogTools.ToDictionary(t => t.Id, t => t, StringComparer.OrdinalIgnoreCase);
        foreach (var tid in bundle.Tools)
        {
            if (lookup.TryGetValue(tid, out var tool))
            {
                StackTools.Add(tool);
            }
        }
    }

    public ToolBundle ToBundle()
    {
        var id = !string.IsNullOrWhiteSpace(StackId)
            ? StackId
            : "custom-" + (string.IsNullOrWhiteSpace(StackName)
                ? Guid.NewGuid().ToString("N")[..8]
                : StackName.ToLowerInvariant().Replace(' ', '-').Replace("/", "").Replace("\\", "") + "-" + Guid.NewGuid().ToString("N")[..4]);

        return new ToolBundle
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(StackName) ? "Custom Stack" : StackName.Trim(),
            Description = StackDescription?.Trim() ?? string.Empty,
            Category = "Custom",
            Icon = "\uE71D",
            Tools = StackTools.Select(t => t.Id).ToList()
        };
    }

    private void RecalculateSize()
    {
        EstimatedSizeBytes = CustomStacksService.EstimateDownloadSizeBytes(
            StackTools.Select(t => t.Id),
            _allCatalogTools);
    }
}
