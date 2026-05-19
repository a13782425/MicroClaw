using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace ShadUI.Demo;

public static class TrayMenuHelper
{
    public static void RegisterTrayIconsEvents(this Application app, MainWindow window, MainWindowViewModel viewModel)
    {
        var trayIcons = TrayIcon.GetIcons(app);
        if (trayIcons is null || !trayIcons.Any()) return;
        var trayIcon = trayIcons[0];

        if (trayIcon.Menu is null) return;

        var items = trayIcon.Menu.Items.OfType<NativeMenuItem>().ToList();
        var openMenu = items.First(x =>
            x.Header != null && x.Header.Contains("Open", StringComparison.CurrentCultureIgnoreCase));
        var aboutMenu = items.First(x =>
            x.Header != null && x.Header.Contains("About", StringComparison.CurrentCultureIgnoreCase));
        var exitMenu = items.First(x =>
            x.Header != null && x.Header.Contains("Exit", StringComparison.CurrentCultureIgnoreCase));

        openMenu.Click += (_, _) =>
        {
            if (window.WindowState == WindowState.Minimized)
            {
                window.RestoreWindowState();
                window.Show();
            }

            window.Activate();
        };

        aboutMenu.Click += (_, _) =>
        {
            if (window.WindowState == WindowState.Minimized)
            {
                window.RestoreWindowState();
                window.Show();
            }

            window.Activate();
            viewModel.ShowAboutCommand.Execute(null);
        };

        exitMenu.Click += (_, _) =>
        {
            if (window.WindowState == WindowState.Minimized)
            {
                window.RestoreWindowState();
                window.Show();
            }

            window.Activate();
            viewModel.TryCloseCommand.Execute(null);
        };
    }
}