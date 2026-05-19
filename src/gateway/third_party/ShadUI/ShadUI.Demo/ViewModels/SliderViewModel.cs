using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ShadUI.Demo.ViewModels;

[Page("slider")]
public sealed partial class SliderViewModel : ViewModelBase, INavigable
{
    private readonly PageManager _pageManager;

    public SliderViewModel(PageManager pageManager)
    {
        _pageManager = pageManager;
        var path = Path.Combine(AppContext.BaseDirectory, "views", "SliderPage.axaml");
        DefaultSliderCode = path.ExtractByLineRange(58, 62).CleanIndentation();
        DisabledSliderCode = path.ExtractByLineRange(65, 70).CleanIndentation();
        TickEnabledSliderCode = path.ExtractByLineRange(73, 79).CleanIndentation();
    }

    [RelayCommand]
    private void BackPage()
    {
        _pageManager.Navigate<SidebarViewModel>();
    }

    [RelayCommand]
    private void NextPage()
    {
        _pageManager.Navigate<SwitchViewModel>();
    }

    [ObservableProperty]
    private string _defaultSliderCode = string.Empty;

    [ObservableProperty]
    private string _disabledSliderCode = string.Empty;

    [ObservableProperty]
    private string _tickEnabledSliderCode = string.Empty;
}