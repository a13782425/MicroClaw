using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShadUI.Demo.Validators;

namespace ShadUI.Demo.ViewModels.Examples.Time;

public sealed partial class FormTimeInputViewModel : ViewModelBase
{
    private readonly ToastManager _toastManager;

    public FormTimeInputViewModel(ToastManager toastManager)
    {
        _toastManager = toastManager;

        var xamlPath = Path.Combine(AppContext.BaseDirectory, "views", "Examples", "Time",
            "FormTimeInputContent.axaml");
        XamlCode = xamlPath.ExtractByLineRange(1, 36).CleanIndentation();

        var csharpPath = Path.Combine(AppContext.BaseDirectory, "viewModels", "Examples", "Time",
            "FormTimeInputViewModel.cs");
        CSharpCode = csharpPath.ExtractWithSkipRanges((17, 24), (26, 31)).CleanIndentation();
    }

    [ObservableProperty]
    private string _xamlCode = string.Empty;

    [ObservableProperty]
    private string _cSharpCode = string.Empty;

    private TimeOnly? _startTime;

    [Required(ErrorMessage = "Start time is required.")]
    [StartTimeValidation(nameof(EndTime), ErrorMessage = "Start time must be less than end time")]
    public TimeOnly? StartTime
    {
        get => _startTime;
        set
        {
            SetProperty(ref _startTime, value, true);
            ValidateProperty(EndTime, nameof(EndTime));
        }
    }

    private TimeOnly? _endTime;

    [Required(ErrorMessage = "End time is required.")]
    [EndTimeValidation(nameof(StartTime), ErrorMessage = "End time must be greater than start time")]
    public TimeOnly? EndTime
    {
        get => _endTime;
        set
        {
            SetProperty(ref _endTime, value, true);
            ValidateProperty(StartTime, nameof(StartTime));
        }
    }

    [RelayCommand]
    private void Submit()
    {
        ClearAllErrors();

        ValidateProperty(StartTime, nameof(StartTime));
        ValidateProperty(EndTime, nameof(EndTime));
        if (HasErrors) return;

        _toastManager.CreateToast("Submission Successful")
            .WithContent(
                $"Your schedule has been created successfully! The start time is {StartTime:hh:mm tt} and the end time is {EndTime:hh:mm tt}.")
            .DismissOnClick()
            .ShowSuccess();
    }
}