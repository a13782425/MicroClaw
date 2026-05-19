using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ShadUI.Demo.ViewModels;

[Page("tab-control")]
public sealed partial class TabControlViewModel : ViewModelBase, INavigable
{
    private readonly PageManager _pageManager;

    public TabControlViewModel(PageManager pageManager)
    {
        _pageManager = pageManager;
        var path = Path.Combine(AppContext.BaseDirectory, "views", "TabControlPage.axaml");
        BasicTabCode = path.ExtractByLineRange(58, 76).CleanIndentation();
    }

    [RelayCommand]
    private void BackPage()
    {
        _pageManager.Navigate<SwitchViewModel>();
    }

    [RelayCommand]
    private void NextPage()
    {
        _pageManager.Navigate<TimeViewModel>();
    }

    [ObservableProperty]
    private string _basicTabCode = string.Empty;
}