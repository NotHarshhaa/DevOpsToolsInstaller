using Windows.UI.ViewManagement;

namespace DevOpsToolsInstaller.Helpers;

/// <summary>
/// Exposes the Windows "reduce animations" accessibility setting
/// (Settings → Accessibility → Visual effects → Animation effects).
/// Connected animations, hover/pulse motion and shimmer placeholders are
/// suppressed when the user turns animations off; content is always shown
/// in its final state instead.
/// </summary>
public static class AnimationSettingsHelper
{
    /// <summary>True when the OS allows animation effects.</summary>
    public static bool AnimationsEnabled { get; }

    static AnimationSettingsHelper()
    {
        try
        {
            AnimationsEnabled = new UISettings().AnimationsEnabled;
        }
        catch
        {
            // If the setting can't be read, err on the side of animating.
            AnimationsEnabled = true;
        }
    }
}
