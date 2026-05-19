using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ShadUI.Demo.ViewModels.Examples.Numeric;

public sealed partial class FormNumericViewModel : ViewModelBase
{
    private readonly ToastManager _toastManager;

    public FormNumericViewModel(ToastManager toastManager)
    {
        _toastManager = toastManager;

        var xamlPath = Path.Combine(AppContext.BaseDirectory, "views", "Examples", "Numeric",
            "FormNumericContent.axaml");
        XamlCode = xamlPath.ExtractByLineRange(1, 47).CleanIndentation();

        var csharpPath = Path.Combine(AppContext.BaseDirectory, "viewModels", "Examples", "Numeric",
            "FormNumericViewModel.cs");
        CSharpCode = csharpPath.ExtractWithSkipRanges((16, 23), (25, 30)).CleanIndentation();
    }

    [ObservableProperty]
    private string _xamlCode = string.Empty;

    [ObservableProperty]
    private string _cSharpCode = string.Empty;

    private double? _age;

    [Required(ErrorMessage = "Age is required")]
    [Range(18d, 100d, ErrorMessage = "Age must be between 18 and 100")]
    public double? Age
    {
        get => _age;
        set => SetProperty(ref _age, value, true);
    }

    [RelayCommand]
    private void Submit()
    {
        ValidateAllProperties();

        if (HasErrors) return;

        _toastManager.CreateToast("Submission Successful")
            .WithContent($"Your age ({Age}) has been submitted successfully!")
            .DismissOnClick()
            .ShowSuccess();
    }

    public void Initialize()
    {
        Age = 0;
        ClearAllErrors();
    }
}