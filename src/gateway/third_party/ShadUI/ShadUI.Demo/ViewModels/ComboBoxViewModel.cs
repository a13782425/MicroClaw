using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShadUI.Demo.ViewModels.Examples.ComboBox;

namespace ShadUI.Demo.ViewModels;

[Page("combobox")]
public sealed partial class ComboBoxViewModel : ViewModelBase, INavigable
{
    private readonly PageManager _pageManager;

    public ComboBoxViewModel(PageManager pageManager, FormComboBoxViewModel form)
    {
        _pageManager = pageManager;
        Form = form;

        var xamlPath = Path.Combine(AppContext.BaseDirectory, "views", "ComboBoxPage.axaml");
        SelectComboBoxCode = xamlPath.ExtractByLineRange(58, 74).CleanIndentation();
        SelectComboBoxDisabledCode = xamlPath.ExtractByLineRange(77, 80).CleanIndentation();
    }

    [RelayCommand]
    private void BackPage()
    {
        _pageManager.Navigate<ColorViewModel>();
    }

    [RelayCommand]
    private void NextPage()
    {
        _pageManager.Navigate<DataTableViewModel>();
    }

    [ObservableProperty]
    private string _selectComboBoxCode = string.Empty;

    [ObservableProperty]
    private string _selectComboBoxDisabledCode = string.Empty;

    [ObservableProperty]
    private FormComboBoxViewModel _form;
}