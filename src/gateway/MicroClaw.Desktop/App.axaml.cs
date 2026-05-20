using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MicroClaw.Desktop.Views;
using ShadUI;
using Window = Avalonia.Controls.Window;

namespace MicroClaw.Desktop;

public partial class App : Application
{
    public static Func<ShadUI.Window>? MainWindowFactory { get; set; }
    
    public static ThemeWatcher ThemeWatcher { get; private set; } = null!;
    

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        ThemeWatcher = new ThemeWatcher(this);
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = MainWindowFactory?.Invoke() ?? CreateFallbackMainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    public static MainView CreateMainView()
    {
        return new MainView
        {
            DataContext = new MainWindowViewModel(),
        };
    }

    private static Window CreateFallbackMainWindow()
    {
        return new Window
        {
            Title = "MicroClaw",
            Width = 1164,
            Height = 768,
            MinWidth = 980,
            MinHeight = 640,
            Content = CreateMainView(),
        };
    }
}
