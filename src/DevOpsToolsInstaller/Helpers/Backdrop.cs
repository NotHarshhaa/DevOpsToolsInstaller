using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace DevOpsToolsInstaller.Helpers;

/// <summary>
/// <c>helpers:Backdrop.Acrylic="True"</c> — draws a subtle in-app Acrylic
/// surface (Windows App SDK 2.x <c>SystemBackdropElement</c>) behind a
/// panel's content, tinted toward the system accent color.
///
/// When Windows transparency effects are disabled or a high-contrast theme
/// is active, no surface element is inserted and the panel's normal
/// background brush shows instead — keeping text contrast AA compliant.
/// Applied once per panel; safe on cached pages whose Loaded fires again.
/// </summary>
public static class Backdrop
{
    public static readonly DependencyProperty AcrylicProperty =
        DependencyProperty.RegisterAttached(
            "Acrylic", typeof(bool), typeof(Backdrop),
            new PropertyMetadata(false, OnAcrylicChanged));

    public static bool GetAcrylic(DependencyObject obj) => (bool)obj.GetValue(AcrylicProperty);
    public static void SetAcrylic(DependencyObject obj, bool value) => obj.SetValue(AcrylicProperty, value);

    private static void OnAcrylicChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Panel panel && (bool)e.NewValue)
        {
            panel.Loaded += AttachSurface;
        }
    }

    private static void AttachSurface(object sender, RoutedEventArgs e)
    {
        var panel = (Panel)sender;
        panel.Loaded -= AttachSurface;

        // Fallback: transparency effects off / high contrast — solid brush only.
        if (!SystemBackdropHelper.TransparencyEffectsEnabled) return;

        // Cached pages raise Loaded on every navigation — attach only once.
        if (panel.Children.Count > 0 && panel.Children[0] is SystemBackdropElement) return;

        // The acrylic must composite directly over the window backdrop to
        // look right — any fill painted behind it would turn it into a flat
        // gray block. The panel's background brush is the no-transparency
        // fallback, so it is only removed when the surface actually attaches.
        panel.Background = null;

        var surface = new SystemBackdropElement
        {
            SystemBackdrop = CreateAccentTintedAcrylic(),
            IsHitTestVisible = false,
            CornerRadius = panel is Grid grid ? grid.CornerRadius : default
        };

        // The surface must cover the whole panel: as an inserted child of a
        // Grid it would otherwise land in cell 0,0 and only paint that row.
        if (panel is Grid hostGrid)
        {
            if (hostGrid.RowDefinitions.Count > 0)
            {
                Grid.SetRow(surface, 0);
                Grid.SetRowSpan(surface, hostGrid.RowDefinitions.Count);
            }
            if (hostGrid.ColumnDefinitions.Count > 0)
            {
                Grid.SetColumn(surface, 0);
                Grid.SetColumnSpan(surface, hostGrid.ColumnDefinitions.Count);
            }
        }

        panel.Children.Insert(0, surface);
    }

    /// <summary>
    /// Creates a thin acrylic backdrop tinted with the system accent color.
    /// Uses reflection for the TintColor property so the app keeps building
    /// against Windows App SDK versions that don't expose it.
    /// </summary>
    private static SystemBackdrop CreateAccentTintedAcrylic()
    {
        var backdrop = new DesktopAcrylicBackdrop();

        try
        {
            var tintProperty = typeof(DesktopAcrylicBackdrop).GetProperty("TintColor");
            if (tintProperty?.PropertyType == typeof(Color) &&
                Application.Current.Resources["SystemAccentColor"] is Color accent)
            {
                // ~15% accent over the standard thin-acrylic tint: subtle,
                // and AA text contrast is unaffected at this opacity.
                tintProperty.SetValue(backdrop, Color.FromArgb(0x26, accent.R, accent.G, accent.B));
            }
        }
        catch
        {
            // Plain acrylic is perfectly fine if the tint API is unavailable.
        }

        return backdrop;
    }
}
