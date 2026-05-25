using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DotNetEnv;
using MicroClaw.Configuration;
using MicroClaw.Desktop.Config;
using MicroClaw.Desktop.Modules;
using MicroClaw.Desktop.ViewModels;
using MicroClaw.Desktop.Views;
using MicroClaw.Runtime;
using Serilog;
using Serilog.Events;
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
            CreateSerilogLogger();
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
        await MicroRuntime.StartAsync(new SerilogMicroLoggerFactory(Serilog.Log.Logger));
        await MicroRuntime.RegisterServiceAsync();
        await MicroRuntime.Engine.RegisterServiceAsync(new MicroViewRouteModule());
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = MainWindowFactory?.Invoke() ?? CreateFallbackMainWindow();
        }
        
        base.OnFrameworkInitializationCompleted();
    }
    
    private static void CreateSerilogLogger()
    {
        LoggingOptions logging = MicroClawConfig.Get<LoggingOptions>();
        var lc = new LoggerConfiguration().MinimumLevel.Is(ParseEnum(logging.MinimumLevel, LogEventLevel.Information)).Enrich.FromLogContext();
        if (logging.File.Enabled)
        {
            lc.WriteTo.File(ResolveLogFilePath(logging.File.Path, MicroClawConfig.HomeDir!), rollingInterval: ParseEnum(logging.File.RollingInterval, RollingInterval.Day), retainedFileCountLimit: logging.File.RetainDays, outputTemplate: logging.File.OutputTemplate);
        }
#if DEBUG
            lc.WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");
#endif
        Serilog.Log.Logger = lc.CreateLogger();
        
        static string ResolveLogFilePath(string path, string baseDir)=>Path.IsPathRooted(path)? path : Path.Combine(baseDir, path.Replace('/', Path.DirectorySeparatorChar));

        static T ParseEnum<T>(string? v, T fallback) where T : struct, Enum => Enum.TryParse<T>(v, ignoreCase: true, out var r) ? r : fallback;
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