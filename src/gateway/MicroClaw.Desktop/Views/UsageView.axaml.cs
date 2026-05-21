using Avalonia.Controls;
using Avalonia.Interactivity;
using ShadUI;

namespace MicroClaw.Desktop.Views;

public partial class UsageView : UserControl
{
    private UsageViewModel? _viewModel;

    public UsageView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not UsageViewModel viewModel)
        {
            return;
        }

        _viewModel = viewModel;
        _viewModel.ThemeWatcher.ThemeChanged += OnThemeChanged;
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.ThemeWatcher.ThemeChanged -= OnThemeChanged;
        }
    }

    private void OnThemeChanged(object? sender, ThemeColors e)
    {
        DailyChart.InvalidateVisual();
        ProviderChart.InvalidateVisual();
    }
}