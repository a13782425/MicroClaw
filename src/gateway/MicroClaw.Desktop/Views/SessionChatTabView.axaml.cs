using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MicroClaw.Desktop.ViewModels;

namespace MicroClaw.Desktop.Views;

public partial class SessionChatTabView : UserControl
{
    private INotifyCollectionChanged? _observedItems;

    public SessionChatTabView()
    {
        InitializeComponent();

        PromptBox.KeyDown += OnPromptKeyDown;
        AttachButton.Click += OnAttachClick;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_observedItems is not null)
            _observedItems.CollectionChanged -= OnItemsChanged;

        if (DataContext is SessionChatTabViewModel vm)
        {
            _observedItems = vm.Items;
            _observedItems.CollectionChanged += OnItemsChanged;
        }
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Defer so the ScrollViewer has measured the newly added item.
        Dispatcher.UIThread.Post(() => MessagesScroll.ScrollToEnd(), DispatcherPriority.Background);
    }

    private void OnPromptKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        // Shift+Enter keeps the default newline behavior.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            return;

        e.Handled = true;
        if (DataContext is SessionChatTabViewModel vm && vm.SendCommand.CanExecute(null))
            vm.SendCommand.Execute(null);
    }

    private async void OnAttachClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SessionChatTabViewModel vm)
            return;

        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        IReadOnlyList<IStorageFile> files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择附件",
            AllowMultiple = true,
        });

        foreach (IStorageFile file in files)
        {
            string name = file.Name;
            string sizeText = await ReadSizeTextAsync(file);
            vm.AddPendingAttachment(new ChatAttachmentVm
            {
                Icon = PickIcon(name),
                Name = name,
                SizeText = sizeText,
                FilePath = file.TryGetLocalPath(),
            });
        }
    }

    private static async Task<string> ReadSizeTextAsync(IStorageFile file)
    {
        try
        {
            StorageItemProperties props = await file.GetBasicPropertiesAsync();
            return props.Size is { } size ? FormatSize(size) : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string FormatSize(ulong bytes)
    {
        if (bytes >= 1024UL * 1024UL)
            return $"{bytes / (1024d * 1024d):0.#} MB";
        if (bytes >= 1024UL)
            return $"{bytes / 1024d:0.#} KB";
        return $"{bytes} B";
    }

    private static string PickIcon(string name)
    {
        string ext = System.IO.Path.GetExtension(name).ToLowerInvariant();
        return ext switch
        {
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".svg" => "🖼",
            ".mp4" or ".mov" or ".mkv" or ".avi" or ".webm" => "🎬",
            ".mp3" or ".wav" or ".flac" or ".m4a" or ".ogg" => "🎵",
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "🗜",
            _ => "📄",
        };
    }
}
