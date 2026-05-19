using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ShadUI.Demo.ViewModels;

[Page("color")]
public sealed partial class ColorViewModel : ViewModelBase, INavigable
{
    private readonly PageManager _pageManager;

    public ColorViewModel(PageManager pageManager)
    {
        _pageManager = pageManager;
        var path = Path.Combine(AppContext.BaseDirectory, "views", "ColorPage.axaml");
        ColorViewCode = path.ExtractByLineRange(59, 64).CleanIndentation();
        ColorPickerCode = path.ExtractByLineRange(67, 72).CleanIndentation();
        ColorPickerDisabledCode = path.ExtractByLineRange(75, 81).CleanIndentation();
    }

    [RelayCommand]
    private void BackPage()
    {
        _pageManager.Navigate<CheckBoxViewModel>();
    }

    [RelayCommand]
    private void NextPage()
    {
        _pageManager.Navigate<ComboBoxViewModel>();
    }

    [ObservableProperty]
    private string _selectedColor = "#3b82f6";

    [ObservableProperty]
    private string _colorViewCode = string.Empty;

    [ObservableProperty]
    private string _colorPickerCode = string.Empty;

    [ObservableProperty]
    private string _colorPickerDisabledCode = string.Empty;
}