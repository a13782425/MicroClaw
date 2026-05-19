using Avalonia.Controls;
using Avalonia.Interactivity;
using ShadUI.Demo.ViewModels;

namespace ShadUI.Demo.Views;

public partial class DashboardPage : UserControl
{
    public DashboardPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.ThemeWatcher.ThemeChanged -= OnThemeChanged;
    }

    private DashboardViewModel? _viewModel;

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DashboardViewModel vm) return;

        _viewModel = vm;
        _viewModel.ThemeWatcher.ThemeChanged += OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, ThemeColors e)
    {
        CartesianChart1.InvalidateVisual();
        CartesianChart2.InvalidateVisual();
    }
}