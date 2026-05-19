using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace ShadUI.Demo.ViewModels;

[Page("tooltip")]
public sealed partial class ToolTipViewModel : ViewModelBase, INavigable
{
    private readonly PageManager _pageManager;

    public ToolTipViewModel(IMessenger messenger, PageManager pageManager)
    {
        _pageManager = pageManager;
        var path = Path.Combine(AppContext.BaseDirectory, "views", "ToolTipPage.axaml");
        UsageCode = path.ExtractByLineRange(58, 65).CleanIndentation();
    }

    [RelayCommand]
    private void BackPage()
    {
        _pageManager.Navigate<ToggleViewModel>();
    }

    [RelayCommand]
    private void NextPage()
    {
        _pageManager.Navigate<MiscellaneousViewModel>();
    }

    [ObservableProperty]
    private string _usageCode = string.Empty;
}