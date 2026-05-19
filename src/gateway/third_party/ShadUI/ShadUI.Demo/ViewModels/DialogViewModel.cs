using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ShadUI.Demo.ViewModels;

[Page("dialog")]
public sealed partial class DialogViewModel : ViewModelBase, INavigable
{
    private readonly PageManager _pageManager;
    private readonly DialogManager _dialogManager;
    private readonly ToastManager _toastManager;
    private readonly LoginViewModel _loginViewModel;

    public DialogViewModel(
        PageManager pageManager,
        DialogManager dialogManager,
        ToastManager toastManager,
        LoginViewModel loginViewModel)
    {
        _pageManager = pageManager;
        _dialogManager = dialogManager;
        _toastManager = toastManager;
        _loginViewModel = loginViewModel;

        var path = Path.Combine(AppContext.BaseDirectory, "viewModels", "DialogViewModel.cs");
        AlertDialogCode = WrapCode(path.ExtractByLineRange(62, 78).CleanIndentation());
        DestructiveAlertDialogCode = WrapCode(path.ExtractByLineRange(83, 100).CleanIndentation());
        CustomDialogCode = WrapCode(path.ExtractByLineRange(105, 122).CleanIndentation());
        InlineDialogCode = WrapCode(path.ExtractByLineRange(129, 140).CleanIndentation());
    }

    [RelayCommand]
    private void BackPage()
    {
        _pageManager.Navigate<DateViewModel>();
    }

    [RelayCommand]
    private void NextPage()
    {
        _pageManager.Navigate<InputViewModel>();
    }

    private string WrapCode(string code)
    {
        return $"""
                using CommunityToolkit.Mvvm.Input;

                //..other code

                {code}

                //..rest of the code

                """;
    }

    [ObservableProperty]
    private string _alertDialogCode = string.Empty;

    [RelayCommand]
    private void ShowDialog()
    {
        _dialogManager
            .CreateDialog(
                "Are you absolutely sure?",
                "This action cannot be undone. This will permanently delete your account and remove your data from our servers.")
            .WithPrimaryButton("Continue",
                () => _toastManager.CreateToast("Delete account")
                    .WithContent("Account deleted successfully!")
                    .DismissOnClick()
                    .ShowSuccess())
            .WithCancelButton("Cancel")
            .WithMaxWidth(512)
            .Dismissible()
            .Show();
    }

    [ObservableProperty]
    private string _destructiveAlertDialogCode = string.Empty;

    [RelayCommand]
    private void ShowDestructiveStyleDialog()
    {
        _dialogManager
            .CreateDialog(
                "Are you absolutely sure?",
                "This action cannot be undone. This will permanently delete your account and remove your data from our servers.")
            .WithPrimaryButton("Continue",
                () => _toastManager.CreateToast("Delete account")
                    .WithContent("Account deleted successfully!")
                    .DismissOnClick()
                    .ShowSuccess()
                , DialogButtonStyle.Destructive)
            .WithCancelButton("Cancel")
            .WithMaxWidth(512)
            .Dismissible()
            .Show();
    }

    [ObservableProperty]
    private string _customDialogCode = string.Empty;

    [RelayCommand]
    private void ShowCustomDialog()
    {
        _loginViewModel.Initialize();
        _dialogManager.CreateDialog(_loginViewModel)
            .Dismissible()
            .WithSuccessCallback(vm =>
                _toastManager.CreateToast("Sign in successful")
                    .WithContent($"Hi {vm.Email}, welcome back!")
                    .DismissOnClick()
                    .ShowSuccess())
            .WithCancelCallback(() =>
                _toastManager.CreateToast("Sign in cancelled")
                    .WithContent("Please sign in to continue.")
                    .DismissOnClick()
                    .ShowWarning())
            .Show();
    }

    public DialogManager InlineDialogManager { get; } = new();

    [ObservableProperty]
    private string _inlineDialogCode = string.Empty;

    [RelayCommand]
    private void ShowInlineDialog()
    {
        InlineDialogManager
            .CreateDialog("In-content dialog",
                "This dialog is hosted inside the card. The page title and sidebar stay interactive while only the card dims.")
            .WithPrimaryButton("OK", () => { })
            .WithCancelButton("Cancel")
            .WithMaxWidth(360)
            .Dismissible()
            .Show();
    }
}