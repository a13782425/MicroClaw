using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ShadUI.Demo.ViewModels.Examples.Date;

public sealed partial class FormDatePickerViewModel : ViewModelBase
{
    private readonly ToastManager _toastManager;

    public FormDatePickerViewModel(ToastManager toastManager)
    {
        _toastManager = toastManager;

        var xamlPath = Path.Combine(AppContext.BaseDirectory, "views", "Examples", "Date",
            "FormDatePickerContent.axaml");
        XamlCode = xamlPath.ExtractByLineRange(1, 28).CleanIndentation();

        var csharpPath = Path.Combine(AppContext.BaseDirectory, "viewModels", "Examples", "Date",
            "FormDatePickerViewModel.cs");
        CSharpCode = csharpPath.ExtractWithSkipRanges((16, 23), (25, 30)).CleanIndentation();
    }

    [ObservableProperty]
    private string _xamlCode = string.Empty;

    [ObservableProperty]
    private string _cSharpCode = string.Empty;

    private DateTime? _selectedDate;

    [Required(ErrorMessage = "A date of birth is required.")]
    public DateTime? SelectedDate
    {
        get => _selectedDate;
        set => SetProperty(ref _selectedDate, value, true);
    }

    [RelayCommand]
    private void Submit()
    {
        ClearAllErrors();

        ValidateProperty(SelectedDate, nameof(SelectedDate));
        if (HasErrors) return;

        _toastManager.CreateToast("Submission Successful")
            .WithContent($"Your date of birth ({SelectedDate:MMM dd, yyyy}) has been submitted successfully!")
            .DismissOnClick()
            .ShowSuccess();
    }
}