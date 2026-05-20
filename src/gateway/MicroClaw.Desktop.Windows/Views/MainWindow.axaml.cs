using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace MicroClaw.Desktop.Windows.Views;

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
            ToggleMaximize();
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

    private void OnToggleMaximizeClicked(object? sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void OnResizeNorthPointerPressed(object? sender, PointerPressedEventArgs e) => BeginResize(WindowEdge.North, e);

    private void OnResizeSouthPointerPressed(object? sender, PointerPressedEventArgs e) => BeginResize(WindowEdge.South, e);

    private void OnResizeWestPointerPressed(object? sender, PointerPressedEventArgs e) => BeginResize(WindowEdge.West, e);

    private void OnResizeEastPointerPressed(object? sender, PointerPressedEventArgs e) => BeginResize(WindowEdge.East, e);

    private void OnResizeNorthWestPointerPressed(object? sender, PointerPressedEventArgs e) => BeginResize(WindowEdge.NorthWest, e);

    private void OnResizeNorthEastPointerPressed(object? sender, PointerPressedEventArgs e) => BeginResize(WindowEdge.NorthEast, e);

    private void OnResizeSouthWestPointerPressed(object? sender, PointerPressedEventArgs e) => BeginResize(WindowEdge.SouthWest, e);

    private void OnResizeSouthEastPointerPressed(object? sender, PointerPressedEventArgs e) => BeginResize(WindowEdge.SouthEast, e);

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void BeginResize(WindowEdge edge, PointerPressedEventArgs e)
    {
        if (!CanResize || WindowState != WindowState.Normal || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        BeginResizeDrag(edge, e);
        e.Handled = true;
    }

    private void UpdateWindowCornerRadius()
    {
        RootCornerRadius = WindowState == WindowState.Maximized ? new CornerRadius(0) : NormalCornerRadius;
    }
}