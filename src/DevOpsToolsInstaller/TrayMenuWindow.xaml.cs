using System;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Graphics;

namespace DevOpsToolsInstaller;

/// <summary>
/// Borderless always-on-top window that shows a WinUI-styled MenuFlyout at
/// the tray icon's cursor position (the tray icon itself is a native
/// Shell_NotifyIcon; this gives it a Fluent context menu with proper
/// keyboard and Narrator support).
/// </summary>
public sealed partial class TrayMenuWindow : Window
{
    private readonly Action _open;
    private readonly Action _catalog;
    private readonly Action _checkUpdates;
    private readonly Action _exit;

    public TrayMenuWindow(Action open, Action catalog, Action checkUpdates, Action exit)
    {
        _open = open;
        _catalog = catalog;
        _checkUpdates = checkUpdates;
        _exit = exit;

        InitializeComponent();

        Title = "DevOps Tools Installer";
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
            presenter.IsResizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        var menu = BuildMenu();
        MenuHost.ContextFlyout = menu;
        menu.Closed += (s, e) => HideWindow();
    }

    private MenuFlyout BuildMenu()
    {
        var menu = new MenuFlyout();

        var open = new MenuFlyoutItem { Text = "Open", Icon = new FontIcon { Glyph = "\uE8A7" } };
        open.Click += (s, e) => { HideWindow(); _open(); };
        menu.Items.Add(open);

        var catalog = new MenuFlyoutItem { Text = "Open Tool Catalog", Icon = new FontIcon { Glyph = "\uE8F1" } };
        catalog.Click += (s, e) => { HideWindow(); _catalog(); };
        menu.Items.Add(catalog);

        var updates = new MenuFlyoutItem { Text = "Check for tool updates", Icon = new FontIcon { Glyph = "\uE895" } };
        updates.Click += (s, e) => { HideWindow(); _checkUpdates(); };
        menu.Items.Add(updates);

        menu.Items.Add(new MenuFlyoutSeparator());

        var exit = new MenuFlyoutItem { Text = "Exit", Icon = new FontIcon { Glyph = "\uE711" } };
        exit.Click += (s, e) => { HideWindow(); _exit(); };
        menu.Items.Add(exit);

        return menu;
    }

    /// <summary>Positions the window under the cursor and opens the flyout.</summary>
    public void ShowAt(Services.TrayIconService.POINT cursor)
    {
        const int width = 240, height = 190;
        var workArea = Microsoft.UI.Windowing.DisplayArea.Primary.WorkArea;
        AppWindow.Resize(new SizeInt32(width, height));

        var x = Math.Min(cursor.X, workArea.X + workArea.Width - width - 8);
        var y = Math.Min(cursor.Y - height - 8, workArea.Y + workArea.Height - height - 8);
        AppWindow.Move(new PointInt32(Math.Max(workArea.X + 8, x), Math.Max(workArea.Y + 8, y)));

        Activate();
        MenuHost.ContextFlyout?.ShowAt(MenuHost, new FlyoutShowOptions
        {
            Placement = FlyoutPlacementMode.TopEdgeAlignedLeft
        });
    }

    /// <summary>Hides the flyout host window (reused on next right-click).</summary>
    public void HideWindow() => AppWindow.Hide();

    public void CloseWindow() => Close();
}
