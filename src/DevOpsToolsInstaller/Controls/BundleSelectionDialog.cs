using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using DevOpsToolsInstaller.Models;

namespace DevOpsToolsInstaller.Controls;

/// <summary>
/// Modern WinUI 3 dialog for browsing and selecting curated tool bundles / stack presets.
/// </summary>
public static class BundleSelectionDialog
{
    public static async Task<ToolBundle?> ShowAsync(XamlRoot xamlRoot, IEnumerable<ToolBundle> bundles, IReadOnlyList<ToolDefinition> allTools)
    {
        if (xamlRoot == null) return null;

        ToolBundle? selectedBundle = null;

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "Curated Tool Stacks",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };

        var rootStack = new StackPanel
        {
            Spacing = 16,
            MaxWidth = 640
        };

        var introText = new TextBlock
        {
            Text = "Select a curated stack to automatically configure your tool selection for a specific workflow or project need.",
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap
        };
        rootStack.Children.Add(introText);

        var scrollViewer = new ScrollViewer
        {
            MaxHeight = 460,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var bundlesList = new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(0, 0, 8, 0)
        };

        foreach (var bundle in bundles)
        {
            var card = new Border
            {
                Background = (Brush)Application.Current.Resources["ControlFillColorDefaultBrush"],
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 12, 14, 12)
            };

            var cardGrid = new Grid
            {
                ColumnSpacing = 12,
                RowSpacing = 8
            };
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            cardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Icon / Emoji
            var iconBox = new Border
            {
                Width = 40,
                Height = 40,
                CornerRadius = new CornerRadius(8),
                Background = (Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"],
                VerticalAlignment = VerticalAlignment.Center
            };
            var iconText = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(bundle.Icon) ? "📦" : bundle.Icon,
                FontSize = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            iconBox.Child = iconText;
            Grid.SetColumn(iconBox, 0);
            Grid.SetRow(iconBox, 0);
            cardGrid.Children.Add(iconBox);

            // Title & Description
            var textStack = new StackPanel
            {
                Spacing = 2,
                VerticalAlignment = VerticalAlignment.Center
            };

            var titleRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center
            };

            var titleText = new TextBlock
            {
                Text = bundle.Name,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 14
            };
            titleRow.Children.Add(titleText);

            var countBadge = new Border
            {
                Background = (Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"],
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(6, 1, 6, 1),
                VerticalAlignment = VerticalAlignment.Center
            };
            var countText = new TextBlock
            {
                Text = $"{bundle.Tools.Count} tools",
                FontSize = 11,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            };
            countBadge.Child = countText;
            titleRow.Children.Add(countBadge);
            textStack.Children.Add(titleRow);

            var descText = new TextBlock
            {
                Text = bundle.Description,
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                TextWrapping = TextWrapping.Wrap
            };
            textStack.Children.Add(descText);

            Grid.SetColumn(textStack, 1);
            Grid.SetRow(textStack, 0);
            cardGrid.Children.Add(textStack);

            // Action Button
            var selectBtn = new Button
            {
                Content = "Select Stack",
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12, 6, 12, 6),
                VerticalAlignment = VerticalAlignment.Center,
                Style = (Style)Application.Current.Resources["AccentButtonStyle"]
            };
            var capturedBundle = bundle;
            selectBtn.Click += (s, e) =>
            {
                selectedBundle = capturedBundle;
                dialog.Hide();
            };

            Grid.SetColumn(selectBtn, 2);
            Grid.SetRow(selectBtn, 0);
            cardGrid.Children.Add(selectBtn);

            // Tool tags wrap panel in row 1
            var toolsPanel = new WrapPanel
            {
                HorizontalSpacing = 4,
                VerticalSpacing = 4,
                Margin = new Thickness(0, 4, 0, 0)
            };

            foreach (var toolId in bundle.Tools)
            {
                var tool = allTools.FirstOrDefault(t => string.Equals(t.Id, toolId, StringComparison.OrdinalIgnoreCase));
                var displayName = tool?.Name ?? toolId;

                var toolPill = new Border
                {
                    Background = (Brush)Application.Current.Resources["ControlFillColorTertiaryBrush"],
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(6, 2, 6, 2)
                };
                var pillText = new TextBlock
                {
                    Text = displayName,
                    FontSize = 11,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                };
                toolPill.Child = pillText;
                toolsPanel.Children.Add(toolPill);
            }

            Grid.SetColumn(toolsPanel, 1);
            Grid.SetColumnSpan(toolsPanel, 2);
            Grid.SetRow(toolsPanel, 1);
            cardGrid.Children.Add(toolsPanel);

            card.Child = cardGrid;
            bundlesList.Children.Add(card);
        }

        scrollViewer.Content = bundlesList;
        rootStack.Children.Add(scrollViewer);
        dialog.Content = rootStack;

        await dialog.ShowAsync();
        return selectedBundle;
    }
}
