using System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace DevOpsToolsInstaller;

/// <summary>
/// Lightweight startup splash screen: borderless, centered on the primary
/// display, closed by the main window once it activates (minimum ~600 ms so
/// it doesn't just flash).
/// </summary>
public sealed partial class SplashWindow : Window
{
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _minDisplayTimer;

    public SplashWindow()
    {
        InitializeComponent();

        Title = "DevOps Tools Installer";
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
            presenter.IsResizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        const int width = 440, height = 320;
        var workArea = DisplayArea.Primary.WorkArea;
        AppWindow.Resize(new SizeInt32(width, height));
        AppWindow.Move(new PointInt32(
            workArea.X + (workArea.Width - width) / 2,
            workArea.Y + (workArea.Height - height) / 2));

        try
        {
            var logoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "app.png");
            if (System.IO.File.Exists(logoPath))
            {
                SplashLogo.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(logoPath));
            }
        }
        catch
        {
            // Logo is decorative.
        }

        // Keep the splash alive at least briefly so it reads as intentional.
        var shown = DateTime.UtcNow;
        _minDisplayTimer = DispatcherQueue.CreateTimer();
        _minDisplayTimer.Interval = TimeSpan.FromMilliseconds(600);
        _minDisplayTimer.Tick += (s, e) =>
        {
            _minDisplayTimer.Stop();
            if ((DateTime.UtcNow - shown).TotalMilliseconds >= 550)
            {
                Close();
            }
        };
    }

    /// <summary>Schedules the splash to close (main window is up).</summary>
    public void BeginClose() => _minDisplayTimer.Start();
}
