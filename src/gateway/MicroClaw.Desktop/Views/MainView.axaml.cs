using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using MicroClaw.Desktop.ViewModels;
using ShadUI;

namespace MicroClaw.Desktop.Views;

public partial class MainView : UserControl
{
    public MainView()
    {   
        InitializeComponent();
        AppSidebar.PropertyChanged += OnSidebarPropertyChanged;
        UpdateFooterLayout(AppSidebar.Expanded);
    }

    private void OnSessionItemTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: SessionNavItem item } && DataContext is MainWindowViewModel vm)
        {
            vm.NavigateCommand.Execute(item.Route);
        }
    }

    private void OnMicroItemTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: MicroNavItem item } && DataContext is MainWindowViewModel vm)
        {
            vm.NavigateCommand.Execute(item.Route);
        }
    }

    private void OnSettingsMenuItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string route } && DataContext is MainWindowViewModel vm)
        {
            vm.NavigateCommand.Execute(route);
        }
    }

    private void OnSidebarPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Sidebar.ExpandedProperty)
        {
            UpdateFooterLayout(AppSidebar.Expanded);
        }
    }
    private void UpdateFooterLayout(bool isExpanded)
    {
        FooterActionsPanel.Orientation = isExpanded ? Orientation.Horizontal : Orientation.Vertical;
        FooterActionsPanel.HorizontalAlignment = isExpanded ? HorizontalAlignment.Right : HorizontalAlignment.Center;
        FooterSettingsMenu.HorizontalAlignment = isExpanded ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
        
        MenuItemAssist.SetPopupPlacement(
            FooterSettingsMenuItem,
            isExpanded ? PlacementMode.TopEdgeAlignedRight : PlacementMode.RightEdgeAlignedBottom);
        
        MenuItemAssist.SetPopupHorizontalOffset(FooterSettingsMenuItem, isExpanded ? 0 : 4);
        MenuItemAssist.SetPopupVerticalOffset(FooterSettingsMenuItem, isExpanded ? -4 : 0);
    }
}