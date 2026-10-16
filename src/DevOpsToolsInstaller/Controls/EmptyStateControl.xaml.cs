using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace DevOpsToolsInstaller.Controls;

/// <summary>
/// Reusable empty-state placeholder shown when a page's backing collection is
/// empty. Set <see cref="Glyph"/>, <see cref="Title"/>, <see cref="Message"/>
/// and (optionally) <see cref="ActionText"/> from XAML, and handle
/// <see cref="ActionClick"/> for the call-to-action button.
/// </summary>
public sealed partial class EmptyStateControl : UserControl
{
    /// <summary>Segoe Fluent Icons glyph shown in the round badge.</summary>
    public string Glyph { get; set; } = "\uE7C3";

    /// <summary>Primary heading.</summary>
    public string Title { get; set; } = "Nothing here yet";

    /// <summary>Supporting sentence explaining what to do. Hidden when empty.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Optional call-to-action button label. Button hidden when empty.</summary>
    public string ActionText { get; set; } = string.Empty;

    /// <summary>Raised when the call-to-action button is clicked.</summary>
    public event RoutedEventHandler? ActionClick;

    public EmptyStateControl()
    {
        InitializeComponent();
        Loaded += (_, _) => Apply();
    }

    private void Apply()
    {
        IconGlyph.Glyph = string.IsNullOrWhiteSpace(Glyph) ? "\uE7C3" : Glyph;
        TitleText.Text = Title;

        MessageText.Text = Message;
        MessageText.Visibility = string.IsNullOrWhiteSpace(Message)
            ? Visibility.Collapsed
            : Visibility.Visible;

        ActionButton.Content = ActionText;
        ActionButton.Visibility = string.IsNullOrWhiteSpace(ActionText)
            ? Visibility.Collapsed
            : Visibility.Visible;

        AutomationProperties.SetName(this, Title);
        AutomationProperties.SetName(ActionButton, ActionText);
    }

    private void ActionButton_Click(object sender, RoutedEventArgs e)
        => ActionClick?.Invoke(this, e);
}
