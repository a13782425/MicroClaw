using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace MicroClaw.Desktop.Mac.Views;

public partial class MainWindow : ShadUI.Window
{
    private static readonly CornerRadius NormalCornerRadius = new(10);

    public MainWindow()
    {
        InitializeComponent();
        UpdateWindowCornerRadius();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty)
        {
            UpdateWindowCornerRadius();
        }
    }

    private void OnChromePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleZoom();
            e.Handled = true;
            return;
        }

        BeginMoveDrag(e);
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnMinimizeClicked(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnToggleZoomClicked(object? sender, RoutedEventArgs e)
    {
        ToggleZoom();
    }

    private void ToggleZoom()
    {
        if (!CanMaximize || !CanResize || WindowState == WindowState.FullScreen)
        {
            return;
        }

        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void UpdateWindowCornerRadius()
    {
        RootCornerRadius = WindowState is WindowState.Maximized or WindowState.FullScreen ? new CornerRadius(0) : NormalCornerRadius;
    }
}