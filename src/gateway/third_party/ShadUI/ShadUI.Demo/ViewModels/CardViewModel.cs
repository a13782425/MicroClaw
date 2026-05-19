using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ShadUI.Demo.ViewModels;

[Page("card")]
public sealed partial class CardViewModel : ViewModelBase, INavigable
{
    private readonly PageManager _pageManager;

    public CardViewModel(PageManager pageManager)
    {
        _pageManager = pageManager;
        var path = Path.Combine(AppContext.BaseDirectory, "views", "CardPage.axaml");
        UsageCode = path.ExtractByLineRange(58, 98).CleanIndentation();
    }

    [RelayCommand]
    private void BackPage()
    {
        _pageManager.Navigate<ButtonViewModel>();
    }

    [RelayCommand]
    private void NextPage()
    {
        _pageManager.Navigate<CheckBoxViewModel>();
    }

    [ObservableProperty]
    private string _usageCode = string.Empty;
}