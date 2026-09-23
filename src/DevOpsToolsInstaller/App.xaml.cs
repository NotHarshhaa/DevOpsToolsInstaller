using Microsoft.UI.Xaml;
using DevOpsToolsInstaller.Services;

namespace DevOpsToolsInstaller;

public partial class App : Application
{
    public static MainWindow? MainWindowInstance { get; private set; }

    public App()
    {
        InitializeComponent();

        // Last-resort safety net: log unexpected exceptions to the activity log
        // so failures are visible on the Downloads page instead of vanishing.
        this.UnhandledException += (s, e) =>
        {
            try
            {
                ActivityLogService.Error("App", $"Unhandled exception: {e.Message}");
            }
            catch
            {
                // Never throw from the handler itself.
            }
            e.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Capture the UI thread's dispatcher so background work (download
        // progress) can marshal PropertyChanged back onto the UI thread.
        UiDispatcher.Queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        SettingsService.LoadSettings();
        FavoritesService.Load();
        MainWindowInstance = new MainWindow();
        MainWindowInstance.Activate();
    }
}
