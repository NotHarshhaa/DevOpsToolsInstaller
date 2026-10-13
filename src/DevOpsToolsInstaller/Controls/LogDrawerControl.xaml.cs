using System;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using DevOpsToolsInstaller.Helpers;
using DevOpsToolsInstaller.Services;
using DevOpsToolsInstaller.ViewModels;

namespace DevOpsToolsInstaller.Controls;

public sealed partial class LogDrawerControl : UserControl
{
    public LogDrawerViewModel ViewModel { get; } = new();

    public bool IsOpen { get; private set; }
    public event Action<bool>? OpenStateChanged;

    private bool _isResizing;
    private double _startPointerY;
    private double _startHeight;

    public LogDrawerControl()
    {
        InitializeComponent();
        Loaded += LogDrawerControl_Loaded;
        Unloaded += LogDrawerControl_Unloaded;

        ViewModel.RequestScrollToBottom += OnRequestScrollToBottom;
    }

    private void LogDrawerControl_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Initialize();

        // Restore filter selection from settings
        var savedFilter = SettingsService.LogDrawerLevelFilter;
        foreach (var item in LevelSegmented.Items)
        {
            if (item is CommunityToolkit.WinUI.Controls.SegmentedItem segItem &&
                (segItem.Tag as string)?.Equals(savedFilter, StringComparison.OrdinalIgnoreCase) == true)
            {
                LevelSegmented.SelectedItem = segItem;
                break;
            }
        }

        // Restore initial open state without animation
        if (SettingsService.LogDrawerIsOpen)
        {
            Open(animate: false);
        }
        else
        {
            Height = 0;
            Visibility = Visibility.Collapsed;
            IsOpen = false;
        }
    }

    private void LogDrawerControl_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Teardown();
    }

    public void Open(bool animate = true)
    {
        if (IsOpen) return;
        IsOpen = true;
        SettingsService.LogDrawerIsOpen = true;
        SettingsService.SaveSettings();

        double targetHeight = SettingsService.LogDrawerHeight >= 120.0
            ? SettingsService.LogDrawerHeight
            : 260.0;

        Visibility = Visibility.Visible;

        if (!animate || !AnimationSettingsHelper.AnimationsEnabled)
        {
            Height = targetHeight;
            OpenStateChanged?.Invoke(true);
            ScrollToBottom();
            return;
        }

        var anim = new DoubleAnimation
        {
            From = 0,
            To = targetHeight,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(anim, this);
        Storyboard.SetTargetProperty(anim, "Height");
        var sb = new Storyboard();
        sb.Children.Add(anim);
        sb.Completed += (s, ev) =>
        {
            Height = targetHeight;
            OpenStateChanged?.Invoke(true);
            ScrollToBottom();
        };
        sb.Begin();
    }

    public void Close(bool animate = true)
    {
        if (!IsOpen) return;
        IsOpen = false;
        SettingsService.LogDrawerIsOpen = false;
        SettingsService.SaveSettings();

        if (!animate || !AnimationSettingsHelper.AnimationsEnabled)
        {
            Height = 0;
            Visibility = Visibility.Collapsed;
            OpenStateChanged?.Invoke(false);
            return;
        }

        var anim = new DoubleAnimation
        {
            From = ActualHeight,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(anim, this);
        Storyboard.SetTargetProperty(anim, "Height");
        var sb = new Storyboard();
        sb.Children.Add(anim);
        sb.Completed += (s, ev) =>
        {
            Height = 0;
            Visibility = Visibility.Collapsed;
            OpenStateChanged?.Invoke(false);
        };
        sb.Begin();
    }

    public void Toggle(bool animate = true)
    {
        if (IsOpen)
        {
            Close(animate);
        }
        else
        {
            Open(animate);
        }
    }

    private void OnRequestScrollToBottom()
    {
        DispatcherQueue.TryEnqueue(ScrollToBottom);
    }

    public void ScrollToBottom()
    {
        if (LogListView.Items.Count > 0)
        {
            LogListView.ScrollIntoView(LogListView.Items[LogListView.Items.Count - 1]);
        }
    }

    // ── Resizing ─────────────────────────────────────────────────────────

    private void ResizeGrip_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isResizing = true;
        _startPointerY = e.GetCurrentPoint(null).Position.Y;
        _startHeight = ActualHeight;
        ResizeGrip.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void ResizeGrip_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isResizing) return;
        var currentY = e.GetCurrentPoint(null).Position.Y;
        var delta = _startPointerY - currentY; // Moving up increases drawer height
        var newHeight = Math.Clamp(_startHeight + delta, 120.0, 650.0);
        Height = newHeight;
        e.Handled = true;
    }

    private void ResizeGrip_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthSouth);
        }
        catch { }
    }

    private void ResizeGrip_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_isResizing)
        {
            try
            {
                ProtectedCursor = null;
            }
            catch { }
        }
    }

    private void ResizeGrip_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_isResizing)
        {
            _isResizing = false;
            ResizeGrip.ReleasePointerCapture(e.Pointer);
            SettingsService.LogDrawerHeight = Height;
            SettingsService.SaveSettings();
            try { ProtectedCursor = null; } catch { }
            e.Handled = true;
        }
    }

    private void ResizeGrip_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _isResizing = false;
        try { ProtectedCursor = null; } catch { }
        SettingsService.LogDrawerHeight = Height;
        SettingsService.SaveSettings();
    }

    // ── Toolbar Actions ──────────────────────────────────────────────────

    private void LevelSegmented_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LevelSegmented.SelectedItem is CommunityToolkit.WinUI.Controls.SegmentedItem item &&
            item.Tag is string tag)
        {
            ViewModel.FilterLevel = tag;
        }
    }

    private void LogSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        ViewModel.SearchText = sender.Text?.Trim() ?? string.Empty;
    }

    private void AutoScrollToggle_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.IsAutoScrollEnabled = AutoScrollToggle.IsChecked == true;
    }

    private void CopyLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = ViewModel.GetClipboardText();
            var dp = new DataPackage();
            dp.SetText(text);
            Clipboard.SetContent(dp);
        }
        catch { }
    }

    private void ClearLogs_Click(object sender, RoutedEventArgs e)
    {
        ActivityLogService.Clear();
    }

    private void CollapseDrawer_Click(object sender, RoutedEventArgs e)
    {
        Close(animate: true);
    }

    public string GetAutoScrollLabel(bool enabled) => enabled ? "Auto-scroll" : "Paused";
}
