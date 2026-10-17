using DevOpsToolsInstaller.Services;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace DevOpsToolsInstaller.Helpers;

/// <summary>
/// Central capability probe + factory for window and in-app Mica/Acrylic
/// surfaces (Windows App SDK 2.x SystemBackdropElement).
///
/// When <see cref="TransparencyEffectsEnabled"/> is false — Windows
/// "Transparency effects" turned off, a high-contrast theme active, or the OS
/// doesn't support system backdrops — every material request resolves to
/// <see cref="WindowMaterial.None"/> and the app falls back to an opaque,
/// theme-aware surface that keeps text contrast AA compliant.
/// </summary>
public static class SystemBackdropHelper
{
    /// <summary>True when in-app system backdrop surfaces may be drawn.</summary>
    public static bool TransparencyEffectsEnabled { get; }

    /// <summary>True when a high-contrast accessibility theme is active.</summary>
    public static bool HighContrast { get; }

    static SystemBackdropHelper()
    {
        try
        {
            var uiSettings = new UISettings();
            var accessibility = new AccessibilitySettings();
            HighContrast = accessibility.HighContrast;
            TransparencyEffectsEnabled =
                uiSettings.AdvancedEffectsEnabled &&         // Windows Settings → Accessibility → Transparency
                !HighContrast &&                             // high-contrast themes: solid colors only
                DesktopAcrylicController.IsSupported() &&    // composition supports in-app acrylic
                MicaController.IsSupported();                // composition supports Mica (window-level)
        }
        catch
        {
            // UISettings can throw on unsupported platforms — degrade to solid surfaces.
            HighContrast = false;
            TransparencyEffectsEnabled = false;
        }
    }

    /// <summary>
    /// Resolves a user choice to the material that can actually be drawn on this
    /// machine: transparency off and high contrast both force
    /// <see cref="WindowMaterial.None"/> (opaque), and Mica falls back to acrylic
    /// when only the acrylic controller is available.
    /// </summary>
    public static WindowMaterial EffectiveMaterial(WindowMaterial requested)
    {
        if (requested == WindowMaterial.None) return WindowMaterial.None;
        if (!TransparencyEffectsEnabled) return WindowMaterial.None;

        if (requested is WindowMaterial.Mica or WindowMaterial.MicaAlt &&
            !MicaController.IsSupported())
        {
            return DesktopAcrylicController.IsSupported()
                ? WindowMaterial.Acrylic
                : WindowMaterial.None;
        }

        return requested;
    }

    /// <summary>
    /// Creates the window-level <see cref="SystemBackdrop"/> for a material, or
    /// null when the material is <see cref="WindowMaterial.None"/> (solid
    /// background) or the OS cannot provide it.
    /// </summary>
    public static SystemBackdrop? CreateWindowBackdrop(WindowMaterial material)
    {
        return EffectiveMaterial(material) switch
        {
            WindowMaterial.Mica => new MicaBackdrop { Kind = MicaKind.Base },
            WindowMaterial.MicaAlt => new MicaBackdrop { Kind = MicaKind.BaseAlt },
            WindowMaterial.Acrylic => new DesktopAcrylicBackdrop(),
            _ => null
        };
    }

    /// <summary>
    /// Creates a backdrop for an in-app <c>SystemBackdropElement</c> (the
    /// sidebar). The pane shares the window's material so the chrome reads as
    /// one continuous surface; the opaque page content behind it is what makes
    /// the pane distinct.
    /// </summary>
    public static SystemBackdrop? CreateSidebarBackdrop(WindowMaterial material)
        => CreateWindowBackdrop(material);
}
