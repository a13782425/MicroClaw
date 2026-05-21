using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using ShadUI;

namespace MicroClaw.Desktop;

public sealed partial class UsageViewModel : ViewModelBase
{
    public UsageViewModel()
    {
        ThemeWatcher = App.ThemeWatcher;

        DailySeries =
        [
            new ColumnSeries<double>
            {
                Values = [420, 560, 610, 590, 710, 820, 1114],
                Fill = new SolidColorPaint(SKColors.Transparent)
            }
        ];

        ProviderSeries =
        [
            new ColumnSeries<double>
            {
                Values = [2480, 1810, 534],
                Fill = new SolidColorPaint(SKColors.Transparent)
            }
        ];

        DailyXAxes =
        [
            new Axis
            {
                Labels = ["周一", "周二", "周三", "周四", "周五", "周六", "周日"],
                LabelsPaint = new SolidColorPaint(SKColors.Gray),
                TextSize = 12,
                MinStep = 1
            }
        ];

        DailyYAxes =
        [
            new Axis
            {
                LabelsPaint = new SolidColorPaint(SKColors.Gray),
                TextSize = 12,
                MinStep = 100,
                ShowSeparatorLines = false
            }
        ];

        ProviderXAxes =
        [
            new Axis
            {
                Labels = ["Claude", "OpenAI", "Local"],
                LabelsPaint = new SolidColorPaint(SKColors.Gray),
                TextSize = 12,
                MinStep = 1
            }
        ];

        ProviderYAxes =
        [
            new Axis
            {
                LabelsPaint = new SolidColorPaint(SKColors.Gray),
                TextSize = 12,
                MinStep = 200,
                ShowSeparatorLines = false
            }
        ];

        ApplyTheme(ThemeWatcher.ThemeColors);
        ThemeWatcher.ThemeChanged += (_, colors) => ApplyTheme(colors);
    }

    public string Title { get; } = "使用情况";

    public string Description { get; } = "先以静态样本展示最近 7 天请求量、模型分布与资源占用。";

    public ThemeWatcher ThemeWatcher { get; }

    public IReadOnlyList<UsageMetric> Metrics { get; } =
    [
        new UsageMetric("7 天请求", "4,824", "用于验证图表与摘要卡片布局"),
        new UsageMetric("Token 总量", "2.1M", "静态样本，不接真实计量接口"),
        new UsageMetric("成功率", "98.4%", "按桌面预览场景模拟")
    ];

    public ISeries[] DailySeries { get; }

    public Axis[] DailyXAxes { get; }

    public Axis[] DailyYAxes { get; }

    public ISeries[] ProviderSeries { get; }

    public Axis[] ProviderXAxes { get; }

    public Axis[] ProviderYAxes { get; }

    [ObservableProperty]
    private SolidColorPaint tooltipTextPaint = new(SKColors.Black);

    [ObservableProperty]
    private SolidColorPaint tooltipBackgroundPaint = new(SKColors.White);

    private void ApplyTheme(ThemeColors colors)
    {
        var foreground = ToSkColor(colors.ForegroundColor, SKColors.Gray);
        var primary = ToSkColor(colors.PrimaryColor, new SKColor(20, 20, 20));
        var primarySoft = ToSkColor(colors.PrimaryColor75, primary);
        var primaryForeground = ToSkColor(colors.PrimaryForegroundColor, SKColors.White);

        TooltipTextPaint = new SolidColorPaint(primaryForeground);
        TooltipBackgroundPaint = new SolidColorPaint(primary);

        DailyXAxes[0].LabelsPaint = new SolidColorPaint(foreground);
        DailyYAxes[0].LabelsPaint = new SolidColorPaint(foreground);
        ProviderXAxes[0].LabelsPaint = new SolidColorPaint(foreground);
        ProviderYAxes[0].LabelsPaint = new SolidColorPaint(foreground);

        ((ColumnSeries<double>)DailySeries[0]).Fill = new SolidColorPaint(primary);
        ((ColumnSeries<double>)ProviderSeries[0]).Fill = new SolidColorPaint(primarySoft);
    }

    private static SKColor ToSkColor(Color color, SKColor fallback)
    {
        return color == default ? fallback : new SKColor(color.R, color.G, color.B, color.A);
    }
}

public sealed record UsageMetric(string Label, string Value, string Caption);