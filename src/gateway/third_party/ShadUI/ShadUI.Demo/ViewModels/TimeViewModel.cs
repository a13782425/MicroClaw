using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShadUI.Demo.ViewModels.Examples.Time;

namespace ShadUI.Demo.ViewModels;

[Page("time")]
public sealed partial class TimeViewModel : ViewModelBase, INavigable
{
    private readonly PageManager _pageManager;

    public TimeViewModel(
        PageManager pageManager,
        FormTimePickerViewModel pickerForm,
        FormTimeInputViewModel inputForm)
    {
        _pageManager = pageManager;
        PickerForm = pickerForm;
        InputForm = inputForm;

        var xamlPath = Path.Combine(AppContext.BaseDirectory, "views", "TimePage.axaml");
        Hour12ClockTimePickerCode = xamlPath.ExtractByLineRange(59, 61).CleanIndentation();
        Hour24ClockTimePickerCode = xamlPath.ExtractByLineRange(64, 67).CleanIndentation();
        DisabledTimePickerCode = xamlPath.ExtractByLineRange(70, 72).CleanIndentation();
        Hour12ClockTimeInputCode = xamlPath.ExtractByLineRange(81, 83).CleanIndentation();
        Hour24ClockTimeInputCode = xamlPath.ExtractByLineRange(86, 89).CleanIndentation();
        DisabledTimeInputCode = xamlPath.ExtractByLineRange(92, 94).CleanIndentation();
    }

    [RelayCommand]
    private void BackPage()
    {
        _pageManager.Navigate<TabControlViewModel>();
    }

    [RelayCommand]
    private void NextPage()
    {
        _pageManager.Navigate<ToastViewModel>();
    }

    [ObservableProperty]
    private string _hour12ClockTimePickerCode = string.Empty;

    [ObservableProperty]
    private string _hour24ClockTimePickerCode = string.Empty;

    [ObservableProperty]
    private string _disabledTimePickerCode = string.Empty;

    [ObservableProperty]
    private FormTimePickerViewModel _pickerForm;

    [ObservableProperty]
    private string _hour12ClockTimeInputCode = string.Empty;

    [ObservableProperty]
    private string _hour24ClockTimeInputCode = string.Empty;

    [ObservableProperty]
    private string _disabledTimeInputCode = string.Empty;

    [ObservableProperty]
    private FormTimeInputViewModel _inputForm;
}