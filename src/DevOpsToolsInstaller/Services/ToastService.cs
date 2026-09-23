using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace DevOpsToolsInstaller.Services;

/// <summary>
/// Shows Windows toast notifications for background events (download /
/// install completions, update availability). Best-effort: any failure to
/// raise a toast (e.g. missing notification support) is silently ignored —
/// the in-app status bar and activity log remain the source of truth.
/// </summary>
public static class ToastService
{
    private const string Aumid = "NotHarshhaa.DevOpsToolsInstaller";

    /// <summary>User preference gate (Settings page).</summary>
    public static bool IsEnabled => SettingsService.EnableNotifications;

    /// <summary>
    /// Shows a toast with a bold title and a supporting message. Safe to call
    /// from any thread; never throws.
    /// </summary>
    public static void Show(string title, string message)
    {
        if (!IsEnabled) return;

        try
        {
            var xml = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText02);
            var texts = xml.GetElementsByTagName("text");
            texts[0].AppendChild(xml.CreateTextNode(title));
            if (texts.Count > 1)
            {
                texts[1].AppendChild(xml.CreateTextNode(message));
            }

            var toast = new ToastNotification(xml);
            ToastNotificationManager.CreateToastNotifier(Aumid).Show(toast);
        }
        catch
        {
            // Toasts are a convenience — never let them break the flow.
        }
    }
}
