using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShadUI.Demo.ViewModels.Examples.ListBox;

namespace ShadUI.Demo.ViewModels;

[Page("miscellaneous")]
public sealed partial class MiscellaneousViewModel : ViewModelBase, INavigable
{
    private readonly PageManager _pageManager;

    public MiscellaneousViewModel(PageManager pageManager, ListBoxViewModel listBox)
    {
        _pageManager = pageManager;
        ListBox = listBox;

        var path = Path.Combine(AppContext.BaseDirectory, "views", "MiscellaneousPage.axaml");
        BusyAreaCode = path.ExtractByLineRange(47, 62).CleanIndentation();
        SkeletonCode = path.ExtractByLineRange(71, 90).CleanIndentation();
    }

    [RelayCommand]
    private void BackPage()
    {
        _pageManager.Navigate<ToolTipViewModel>();
    }

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyAreaCode = string.Empty;

    [RelayCommand]
    private async Task ToggleBusy()
    {
        IsBusy = true;
        await Task.Delay(3000);
        IsBusy = false;
    }

    [ObservableProperty]
    private ListBoxViewModel _listBox;

    [ObservableProperty]
    private string _skeletonCode = string.Empty;
}