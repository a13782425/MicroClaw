using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using Avalonia.Controls.Selection;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ShadUI.Demo.ViewModels.Examples.ListBox;

public sealed partial class ListBoxViewModel : ViewModelBase
{
    public ListBoxViewModel()
    {
        var xamlPath = Path.Combine(AppContext.BaseDirectory, "views", "Examples", "ListBox",
            "ListBoxContent.axaml");
        XamlCode = xamlPath.ExtractByLineRange(1, 41).CleanIndentation();

        var csharpPath = Path.Combine(AppContext.BaseDirectory, "viewModels", "Examples", "ListBox",
            "ListBoxViewModel.cs");
        CSharpCode = csharpPath.ExtractWithSkipRanges((14, 21), (25, 30)).CleanIndentation();

        _selectedItems.CollectionChanged += OnSelectedItemsOnCollectionChanged;
        SelectionModel.SelectionChanged += SelectedWebFrameworksChanged;
    }

    [ObservableProperty]
    private string _xamlCode = string.Empty;

    [ObservableProperty]
    private string _cSharpCode = string.Empty;

    private string _selectionMode = "Single";

    public string SelectionMode
    {
        get => _selectionMode;
        set => SetProperty(ref _selectionMode, value);
    }

    [ObservableProperty]
    private string[] _selectionModes = ["Single", "Multiple", "Toggle", "Always Selected"];

    [ObservableProperty]
    private string _selectedCount = "No frameworks selected";

    [ObservableProperty]
    private string[] _items =
    [
        "Angular",
        "Astro",
        "Lit",
        "Next.js",
        "Nuxt.js",
        "Preact",
        "Qwik",
        "React",
        "Remix",
        "SolidJS",
        "Svelte",
        "SvelteKit",
        "Vue.js"
    ];

    [ObservableProperty]
    private ISelectionModel _selectionModel = new SelectionModel<string>();

    [ObservableProperty]
    private string _listBoxCode = string.Empty;

    private readonly ObservableCollection<string> _selectedItems = [];

    private void OnSelectedItemsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SelectedCount = _selectedItems.Count switch
        {
            0 => "No frameworks selected",
            1 => $"1 framework selected: {_selectedItems[0]}",
            _ => $"{_selectedItems.Count} frameworks selected"
        };
    }

    private void SelectedWebFrameworksChanged(object? sender, SelectionModelSelectionChangedEventArgs e)
    {
        var selectedItems = e.SelectedItems;
        var deselectedItems = e.DeselectedItems;

        if (selectedItems.Count > 0)
        {
            foreach (var i in selectedItems)
            {
                if (i is not string s) continue;
                if (!_selectedItems.Contains(s)) _selectedItems.Add(s);
            }
        }

        if (deselectedItems.Count > 0)
        {
            foreach (var i in deselectedItems)
            {
                if (i is not string s) continue;
                _selectedItems.Remove(s);
            }
        }
    }
}