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
        SettingsService.LoadSettings();
        MainWindowInstance = new MainWindow();
        MainWindowInstance.Activate();
    }
}
