using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShadUI.Demo.Validators;

namespace ShadUI.Demo.ViewModels.Examples.Input;

public sealed partial class FormInputViewModel : ViewModelBase
{
    private readonly ToastManager _toastManager;

    public FormInputViewModel(ToastManager toastManager)
    {
        _toastManager = toastManager;

        var xamlPath = Path.Combine(AppContext.BaseDirectory, "views", "Examples", "Input",
            "FormInputContent.axaml");
        XamlCode = xamlPath.ExtractByLineRange(1, 47).CleanIndentation();

        var csharpPath = Path.Combine(AppContext.BaseDirectory, "viewModels", "Examples", "Input",
            "FormInputViewModel.cs");
        CSharpCode = csharpPath.ExtractWithSkipRanges((17, 24), (26, 31)).CleanIndentation();
    }

    [ObservableProperty]
    private string _xamlCode = string.Empty;

    [ObservableProperty]
    private string _cSharpCode = string.Empty;

    private string _email = string.Empty;

    [Required(ErrorMessage = "Email is required")]
    [EmailValidation]
    public string Email
    {
        get => _email;
        set => SetProperty(ref _email, value, true);
    }

    private string _password = string.Empty;

    [Required(ErrorMessage = "Password is required")]
    [MinLength(8, ErrorMessage = "Password must be at least 8 characters long")]
    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value, true);
    }

    private string _confirmPassword = string.Empty;

    [Required(ErrorMessage = "Confirm password is required")]
    [IsMatchWith(nameof(Password), ErrorMessage = "Passwords do not match")]
    public string ConfirmPassword
    {
        get => _confirmPassword;
        set => SetProperty(ref _confirmPassword, value, true);
    }

    [RelayCommand]
    private void Submit()
    {
        ClearAllErrors();
        ValidateAllProperties();

        if (HasErrors) return;

        _toastManager.CreateToast("Submission Successful")
            .WithContent("Your form has been submitted successfully! All data has been processed and saved.")
            .DismissOnClick()
            .ShowSuccess();

        Initialize();
    }

    public void Initialize()
    {
        Email = string.Empty;
        Password = string.Empty;
        ConfirmPassword = string.Empty;

        ClearAllErrors();
    }
}