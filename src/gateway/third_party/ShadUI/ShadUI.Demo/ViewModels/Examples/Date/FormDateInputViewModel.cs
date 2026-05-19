using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShadUI.Demo.Validators;

namespace ShadUI.Demo.ViewModels.Examples.Date;

public sealed partial class FormDateInputViewModel : ViewModelBase
{
    private readonly ToastManager _toastManager;

    public FormDateInputViewModel(ToastManager toastManager)
    {
        _toastManager = toastManager;

        var xamlPath = Path.Combine(AppContext.BaseDirectory, "views", "Examples", "Date",
            "FormDateInputContent.axaml");
        XamlCode = xamlPath.ExtractByLineRange(1, 36).CleanIndentation();

        var csharpPath = Path.Combine(AppContext.BaseDirectory, "viewModels", "Examples", "Date",
            "FormDateInputViewModel.cs");
        CSharpCode = csharpPath.ExtractWithSkipRanges((17, 24), (26, 31)).CleanIndentation();
    }

    [ObservableProperty]
    private string _xamlCode = string.Empty;

    [ObservableProperty]
    private string _cSharpCode = string.Empty;

    private DateOnly? _startDate;

    [Required(ErrorMessage = "Start date is required.")]
    [StartDateValidation(nameof(EndDate), ErrorMessage = "Start date must be less than end date")]
    public DateOnly? StartDate
    {
        get => _startDate;
        set
        {
            SetProperty(ref _startDate, value, true);
            ValidateProperty(EndDate, nameof(EndDate));
        }
    }

    private DateOnly? _endDate;

    [Required(ErrorMessage = "End date is required.")]
    [EndDateValidation(nameof(StartDate), ErrorMessage = "End date must be greater than start date")]
    public DateOnly? EndDate
    {
        get => _endDate;
        set
        {
            SetProperty(ref _endDate, value, true);
            ValidateProperty(StartDate, nameof(StartDate));
        }
    }

    [RelayCommand]
    private void Submit()
    {
        ClearAllErrors();

        ValidateProperty(StartDate, nameof(StartDate));
        ValidateProperty(EndDate, nameof(EndDate));
        if (HasErrors) return;

        _toastManager.CreateToast("Submission Successful")
            .WithContent(
                $"Your schedule has been created successfully! The start date is {StartDate:MMM dd, yyyy} and the end date is {EndDate:MMM dd, yyyy}.")
            .DismissOnClick()
            .ShowSuccess();
    }
}