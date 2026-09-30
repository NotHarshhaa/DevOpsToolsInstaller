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

        // Headless CLI mode: run the requested command and exit without
        // creating a window (used by provisioning scripts and CI).
        var cliArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();
        if (Services.CliHost.IsCliRequest(cliArgs))
        {
            _ = RunCliAsync(cliArgs);
            return;
        }

        // Splash screen: shown until the main window activates (>= ~600 ms).
        SplashWindow? splash = null;
        if (!Services.CliHost.IsCliRequest(cliArgs))
        {
            splash = new SplashWindow();
            splash.Activate();
        }

        MainWindowInstance = new MainWindow();
        MainWindowInstance.Activate();
        splash?.BeginClose();

        // Register for app notifications (toasts). Done after window creation
        // so the process is fully initialized; for unpackaged installs this
        // also creates the AUMID registration.
        ToastService.Register();
    }

    private async System.Threading.Tasks.Task RunCliAsync(string[] args)
    {
        var exitCode = await Services.CliHost.RunAsync(args);
        Exit();
        Environment.ExitCode = exitCode;
    }
}
