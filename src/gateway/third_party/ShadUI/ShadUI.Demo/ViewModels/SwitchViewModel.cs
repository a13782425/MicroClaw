using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ShadUI.Demo.ViewModels;

[Page("switch")]
public sealed partial class SwitchViewModel : ViewModelBase, INavigable
{
    private readonly PageManager _pageManager;

    public SwitchViewModel(PageManager pageManager)
    {
        _pageManager = pageManager;
        var path = Path.Combine(AppContext.BaseDirectory, "views", "SwitchPage.axaml");
        EnableCode = path.ExtractByLineRange(58, 76).CleanIndentation();
        DisableCode = path.ExtractByLineRange(79, 97).CleanIndentation();
        RightAlignedCode = path.ExtractByLineRange(100, 118).CleanIndentation();
    }

    [RelayCommand]
    private void BackPage()
    {
        _pageManager.Navigate<SliderViewModel>();
    }

    [RelayCommand]
    private void NextPage()
    {
        _pageManager.Navigate<TabControlViewModel>();
    }

    [ObservableProperty]
    private string _enableCode = string.Empty;

    [ObservableProperty]
    private string _disableCode = string.Empty;

    [ObservableProperty]
    private string _rightAlignedCode = string.Empty;
}