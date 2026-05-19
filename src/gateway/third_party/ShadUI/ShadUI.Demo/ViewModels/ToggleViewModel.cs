using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace ShadUI.Demo.ViewModels;

[Page("toggle")]
public sealed partial class ToggleViewModel : ViewModelBase, INavigable
{
    private readonly PageManager _pageManager;

    public ToggleViewModel(PageManager pageManager)
    {
        _pageManager = pageManager;
        var path = Path.Combine(AppContext.BaseDirectory, "views", "TogglePage.axaml");
        DefaultCode = path.ExtractByLineRange(58, 63).CleanIndentation();
        OutlineCode = path.ExtractByLineRange(66, 71).CleanIndentation();
        WithTextCode = path.ExtractByLineRange(74, 82).CleanIndentation();
        DisableCode = path.ExtractByLineRange(85, 91).CleanIndentation();
    }

    [RelayCommand]
    private void BackPage()
    {
        _pageManager.Navigate<ToastViewModel>();
    }

    [RelayCommand]
    private void NextPage()
    {
        _pageManager.Navigate<ToolTipViewModel>();
    }

    [ObservableProperty]
    private string _defaultCode = string.Empty;

    [ObservableProperty]
    private string _outlineCode = string.Empty;

    [ObservableProperty]
    private string _withTextCode = string.Empty;

    [ObservableProperty]
    private string _disableCode = string.Empty;
}