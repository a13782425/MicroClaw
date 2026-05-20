using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MicroClaw.Desktop.Views;

namespace MicroClaw.Desktop;

public partial class App : Application
{
    public static Func<Window>? MainWindowFactory { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {  
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
