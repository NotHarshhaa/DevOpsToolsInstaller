using Microsoft.UI.Composition.SystemBackdrops;
using Windows.UI.ViewManagement;

namespace DevOpsToolsInstaller.Helpers;

/// <summary>
/// Central capability probe for in-app Mica/Acrylic surfaces
/// (Windows App SDK 2.x SystemBackdropElement).
///
/// When this returns false — Windows "Transparency effects" turned off,
/// a high-contrast theme active, or the OS doesn't support system
/// backdrops — in-app surfaces are skipped entirely and the fallback
/// solid/translucent brushes (<see cref="SystemBackdropHelper"/>) keep
/// text contrast AA compliant.
/// </summary>
public static class SystemBackdropHelper
{
    /// <summary>True when in-app system backdrop surfaces may be drawn.</summary>
    public static bool TransparencyEffectsEnabled { get; }

    static SystemBackdropHelper()
    {
        try
        {
            var uiSettings = new UISettings();
            var accessibility = new AccessibilitySettings();
            TransparencyEffectsEnabled =
                uiSettings.AdvancedEffectsEnabled &&         // Windows Settings → Accessibility → Transparency
                !accessibility.HighContrast &&               // high-contrast themes: solid colors only
                DesktopAcrylicController.IsSupported() &&    // composition supports in-app acrylic
                MicaController.IsSupported();                // composition supports Mica (window-level)
        }
        catch
        {
            // UISettings can throw on unsupported platforms — degrade to solid surfaces.
            TransparencyEffectsEnabled = false;
        }
    }
}
