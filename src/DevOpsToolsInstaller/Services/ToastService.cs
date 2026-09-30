using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace DevOpsToolsInstaller.Services;

/// <summary>
/// Shows Windows toast notifications for background events (download /
/// install completions, update availability), using the Windows App SDK
/// app-notifications API (Microsoft.Windows.AppNotifications). Best-effort:
/// any failure to raise a toast (e.g. missing notification support) is
/// silently ignored — the in-app status bar and activity log remain the
/// source of truth.
/// </summary>
public static class ToastService
{
    /// <summary>User preference gate (Settings page).</summary>
    public static bool IsEnabled => SettingsService.EnableNotifications;

    /// <summary>
    /// Registers the app with the notification platform. Must be called once
    /// before the first <see cref="Show"/>; for unpackaged installs it also
    /// creates the AppUserModelId registration toasts require.
    /// </summary>
    public static void Register()
    {
        try
        {
            AppNotificationManager.Default.Register();
        }
        catch
        {
            // Registration is a convenience — never let it break startup.
        }
    }

    /// <summary>
    /// Shows a toast with a bold title and a supporting message. Safe to call
    /// from any thread; never throws.
    /// </summary>
    public static void Show(string title, string message)
    {
        if (!IsEnabled) return;

        try
        {
            var notification = new AppNotificationBuilder()
                .AddText(title)
                .AddText(message)
                .BuildNotification();
            AppNotificationManager.Default.Show(notification);
        }
        catch
        {
            // Toasts are a convenience — never let them break the flow.
        }
    }

    /// <summary>
    /// Removes the notification registration (call on application exit so
    /// stale notifications don't try to activate a dead process).
    /// </summary>
    public static void Shutdown()
    {
        try
        {
            AppNotificationManager.Default.UnregisterAll();
        }
        catch
        {
        }
    }
}
