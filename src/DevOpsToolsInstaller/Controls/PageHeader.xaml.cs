using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DevOpsToolsInstaller.Controls;

/// <summary>
/// Shared page header used by every page so titles, badges and left edges line
/// up: a title, an optional count/status badge, an optional subtitle and an
/// optional set of right-aligned actions (<see cref="ActionsContent"/>).
/// Below the wide breakpoint the actions automatically drop onto their own row.
/// </summary>
public sealed partial class PageHeader : UserControl
{
    public PageHeader()
    {
        InitializeComponent();
        Loaded += (_, _) => Apply();
    }

    private string _title = string.Empty;

    /// <summary>Primary page title.</summary>
    public string Title
    {
        get => _title;
        set { _title = value ?? string.Empty; Apply(); }
    }

    private string _subtitle = string.Empty;

    /// <summary>Supporting sentence under the title. Hidden when empty.</summary>
    public string Subtitle
    {
        get => _subtitle;
        set { _subtitle = value ?? string.Empty; Apply(); }
    }

    private string _badgeText = string.Empty;

    /// <summary>Optional count/status badge next to the title. Hidden when empty.</summary>
    public string BadgeText
    {
        get => _badgeText;
        set { _badgeText = value ?? string.Empty; Apply(); }
    }

    private object? _actionsContent;

    /// <summary>Optional right-aligned actions (typically a StackPanel of buttons).</summary>
    public object? ActionsContent
    {
        get => _actionsContent;
        set { _actionsContent = value; Apply(); }
    }

    private void Apply()
    {
        // Property setters can run before the visual tree exists in theory; the
        // Loaded handler re-applies once everything is realized.
        if (TitleText is null) return;

        if (!string.IsNullOrWhiteSpace(_title)) TitleText.Text = _title;

        SubtitleText.Text = _subtitle;
        SubtitleText.Visibility = string.IsNullOrWhiteSpace(_subtitle)
            ? Visibility.Collapsed
            : Visibility.Visible;

        BadgeTextBlock.Text = _badgeText;
        BadgeBorder.Visibility = string.IsNullOrWhiteSpace(_badgeText)
            ? Visibility.Collapsed
            : Visibility.Visible;

        ActionsHost.Content = _actionsContent;
    }
}
