using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.Input;
using ShadUI.Demo.Validators;

namespace ShadUI.Demo.ViewModels;

public sealed partial class LoginViewModel(DialogManager dialogManager) : ViewModelBase
{
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

    private bool CanSubmit()
    {
        return !HasErrors;
    }

    public void Initialize()
    {
        Email = string.Empty;
        Password = string.Empty;

        ClearAllErrors();
    }

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private void Submit()
    {
        ClearAllErrors();
        ValidateAllProperties();

        if (HasErrors) return;
        dialogManager.Close(this, new CloseDialogOptions { Success = true });
    }

    [RelayCommand]
    private void Cancel()
    {
        dialogManager.Close(this);
    }
}