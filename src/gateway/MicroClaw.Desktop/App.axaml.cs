using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DotNetEnv;
using MicroClaw.Configuration;
using MicroClaw.Desktop.Views;
using MicroClaw.Runtime;
using ShadUI;
using Window = Avalonia.Controls.Window;

namespace MicroClaw.Desktop;
public partial class App : Application
{
    public static Func<ShadUI.Window>? MainWindowFactory { get; set; }
    
    public static ThemeWatcher ThemeWatcher { get; private set; } = null!;
    
    
    public override void Initialize()
    {
        // 读取本地 .env 文件（可指定路径，也可默认当前目录下 .env）
        try
        {
            Env.Load(); // 或 Env.Load(".env.local");
            MicroClawConfig.Initialize();
            
        }
        catch (Exception ex)
        {
            // 可选：记录日志或忽略
        }
        AvaloniaXamlLoader.Load(this);
    }
    
    public override async void OnFrameworkInitializationCompleted()
    {
        ThemeWatcher = new ThemeWatcher(this);
        ThemeWatcher.Initialize();
        await MicroRuntime.StartAsync();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = MainWindowFactory?.Invoke() ?? CreateFallbackMainWindow();
        }
        
        base.OnFrameworkInitializationCompleted();
    }
    
    public static MainView CreateMainView()
    {
        return new MainView { DataContext = new MainWindowViewModel(), };
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