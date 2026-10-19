using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Services.Store;

namespace DevOpsToolsInstaller.Services;

/// <summary>Result of checking the Microsoft Store for an app update.</summary>
/// <param name="IsUpdateAvailable">True when the Store has a newer package than the running one.</param>
/// <param name="CurrentVersion">The version of the currently running packaged app.</param>
public sealed record StoreAppUpdate(
    bool IsUpdateAvailable,
    string CurrentVersion);

/// <summary>
/// App-update story for <b>Microsoft Store installations</b> (packaged MSIX with
/// package identity). When the app ships through the Store, GitHub self-updates
/// must never run
/// — the Store is the sole, correct update channel because it verifies and
/// signs updates, and GitHub releases carry no package identity for this
/// install. This service talks to <see cref="StoreContext"/> instead:
/// <see cref="CheckAsync"/> lists pending Store package updates for the current
/// app, <see cref="InstallNowAsync"/> shows the Store's own install prompt, and
/// <see cref="OpenStorePageAsync"/> opens the product page as a fallback.
/// </summary>
public static class StoreUpdateService
{
    /// <summary>
    /// Checks the Microsoft Store for pending package updates for the running
    /// app. Never throws: on any failure the result simply reports "no update",
    /// so the UI can say "updates are managed by the Store" instead of an error.
    /// </summary>
    public static async Task<StoreAppUpdate> CheckAsync()
    {
        var version = GetRunningVersion();

        // Store checks are meaningless without package identity (which is what
        // "installed from the Store" guarantees).
        if (!AppUpdaterService.IsRunningAsPackagedApp)
            return new StoreAppUpdate(false, version);

        try
        {
            var context = StoreContext.GetDefault();
            var updates = await context.GetAppAndOptionalStorePackageUpdatesAsync();
            SettingsService.LastUpdateCheckTime = DateTime.Now;
            SettingsService.SaveSettings();

            return new StoreAppUpdate(updates.Count > 0, version);
        }
        catch
        {
            // Offline, Store access restricted, or identity issues: report
            // "nothing found" rather than failing the check.
            return new StoreAppUpdate(false, version);
        }
    }

    /// <summary>
    /// Opens the Store's install prompt (and downloads + replaces the app if the
    /// user confirms). Returns false when there was nothing to install or the
    /// flow was cancelled/failed.
    /// </summary>
    public static async Task<bool> InstallNowAsync()
    {
        if (!AppUpdaterService.IsRunningAsPackagedApp) return false;

        try
        {
            var context = InitializeWithWindow(StoreContext.GetDefault());
            var updates = await context.GetAppAndOptionalStorePackageUpdatesAsync();
            if (updates.Count == 0) return false;

            // The Store shows its own confirmation dialog and then downloads,
            // installs and restarts the app.
            var result = await context.RequestDownloadAndInstallStorePackageUpdatesAsync(updates);
            var state = result.OverallState;
            return state is StorePackageUpdateState.Completed or
                StorePackageUpdateState.Pending or
                StorePackageUpdateState.Downloading;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Opens the app's product page in the Microsoft Store so the user can
    /// update from there. Used as the fallback when the direct install prompt
    /// is unavailable. Returns false when the product page cannot be resolved
    /// (e.g. the package does not (yet) have a Store product id).
    /// </summary>
    public static async Task<bool> OpenStorePageAsync()
    {
        if (!AppUpdaterService.IsRunningAsPackagedApp) return false;

        try
        {
            var context = StoreContext.GetDefault();
            var productResult = await context.GetStoreProductForCurrentAppAsync();
            if (productResult.ExtendedError is not null ||
                string.IsNullOrWhiteSpace(productResult.Product?.StoreId))
            {
                return false;
            }

            var uri = new Uri(
                $"ms-windows-store://pdp/?ProductId={productResult.Product.StoreId}");
            return await Windows.System.Launcher.LaunchUriAsync(uri);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>"1.0.3.0" style version of the running packaged app.</summary>
    private static string GetRunningVersion()
    {
        try
        {
            var v = Package.Current.Id.Version;
            return $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
        }
        catch
        {
            return AppUpdaterService.CurrentVersion;
        }
    }

    /// <summary>
    /// Shows the Store's confirmation dialog on the app's window (required for
    /// desktop apps; StoreContext otherwise has no window to parent its UI).
    /// </summary>
    private static StoreContext InitializeWithWindow(StoreContext context)
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
            if (hwnd != IntPtr.Zero)
                ((IInitializeWithWindow)(object)context).Initialize(hwnd);
        }
        catch
        {
            // No window handle — the direct install will be unavailable but
            // OpenStorePageAsync remains as the fallback path.
        }
        return context;
    }

    /// <summary>WinRT <c>IInitializeWithWindow</c> interop for desktop apps.</summary>
    [ComImport]
    [Guid("3E68D4BD-7134-4D47-9138-443F5B6BE405")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IInitializeWithWindow
    {
        void Initialize([In] IntPtr hwnd);
    }
}
