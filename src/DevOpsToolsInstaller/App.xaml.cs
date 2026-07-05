using Microsoft.UI.Xaml;
using DevOpsToolsInstaller.Services;

namespace DevOpsToolsInstaller;

public partial class App : Application
{
    public static MainWindow? MainWindowInstance { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Capture the UI thread's dispatcher so background work (download
        // progress) can marshal PropertyChanged back onto the UI thread.
        UiDispatcher.Queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        SettingsService.LoadSettings();
        MainWindowInstance = new MainWindow();
        MainWindowInstance.Activate();
    }
}
