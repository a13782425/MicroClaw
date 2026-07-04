using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text;
using Avalonia.Layout;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MicroClaw.Common;
using MicroClaw.Providers;
using MicroClaw.Runtime;
using Microsoft.Extensions.AI;

namespace MicroClaw.Desktop.ViewModels;

/// <summary>
/// Chat tab view-model. Keeps the whole conversation in memory only (no persistence)
/// and drives replies through the real default chat provider.
/// </summary>
[PageRoute(PageRouteDefine.RouteSessionChat)]
public partial class SessionChatTabViewModel : RouteViewModelBase
{
    private readonly ModelProviderService _providers = MicroRuntime.Engine.GetRequiredService<ModelProviderService>();

    // Real conversation history handed to the model (Microsoft.Extensions.AI messages).
    private readonly List<ChatMessage> _history = [];

    private DesktopChatSession _session = new(SessionViewModel.DefaultSessionId);
    private DateOnly? _lastDate;

    /// <summary>Heterogeneous list: date separators + chat messages.</summary>
    public ObservableCollection<object> Items { get; } = [];

    /// <summary>Files staged in the input area, waiting to be sent (memory only).</summary>
    public ObservableCollection<ChatAttachmentVm> PendingAttachments { get; } = [];

    [ObservableProperty]
    private string _draftPrompt = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool _isSending;

    public bool HasPendingAttachments => PendingAttachments.Count > 0;

    public SessionChatTabViewModel()
    {
        PendingAttachments.CollectionChanged += OnPendingAttachmentsChanged;
    }

    protected internal override void OnNavigated(object? parameter)
    {
        if (parameter is string sessionId && !string.IsNullOrWhiteSpace(sessionId))
            _session = new DesktopChatSession(sessionId);
    }

    private void OnPendingAttachmentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => OnPropertyChanged(nameof(HasPendingAttachments));

    /// <summary>Adds a staged attachment. Called from the view after the file picker returns.</summary>
    public void AddPendingAttachment(ChatAttachmentVm attachment)
    {
        if (attachment is not null)
            PendingAttachments.Add(attachment);
    }

    [RelayCommand]
    private void RemovePendingAttachment(ChatAttachmentVm? attachment)
    {
        if (attachment is not null)
            PendingAttachments.Remove(attachment);
    }

    private bool CanSend() => !IsSending;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        string text = (DraftPrompt ?? string.Empty).Trim();
        if (text.Length == 0 && PendingAttachments.Count == 0)
            return;

        // ── User message (real input + staged files) ─────────────────────
        DateTimeOffset userTime = DateTimeOffset.Now;
        ChatMessageVm userMsg = new()
        {
            IsFromUser = true,
            Text = text,
            Time = FormatTime(userTime),
            TimeTooltip = FormatTimeTooltip(userTime),
        };
        foreach (ChatAttachmentVm attachment in PendingAttachments)
            userMsg.Attachments.Add(attachment);
        AppendMessage(userMsg);

        _history.Add(new ChatMessage(ChatRole.User, text.Length > 0 ? text : "(附件)"));

        DraftPrompt = string.Empty;
        PendingAttachments.Clear();

        // ── Assistant reply via real default chat provider ───────────────
        IsSending = true;
        try
        {
            string reply;
            string? thinking = null;

            ChatModelClient? chat = _providers.GetDefaultChat();
            if (chat is null)
            {
                reply = "尚未配置可用的聊天模型 Provider，请先在「设置 · 模型提供方」中添加并设为默认。";
            }
            else
            {
                MicroChatContext ctx = new()
                {
                    Session = _session,
                    Source = "desktop",
                    Ct = CancellationToken.None,
                    Messages = _history,
                };
                ChatResponse response = await chat.ChatAsync(ctx, _history, null);
                reply = string.IsNullOrWhiteSpace(response.Text) ? "（无回复）" : response.Text;
                thinking = ExtractReasoning(response);
                _history.Add(new ChatMessage(ChatRole.Assistant, reply));
            }

            DateTimeOffset replyTime = DateTimeOffset.Now;
            AppendMessage(new ChatMessageVm
            {
                IsFromUser = false,
                Text = reply,
                Thinking = thinking,
                Time = FormatTime(replyTime),
                TimeTooltip = FormatTimeTooltip(replyTime),
            });
        }
        catch (Exception ex)
        {
            DateTimeOffset errorTime = DateTimeOffset.Now;
            AppendMessage(new ChatMessageVm
            {
                IsFromUser = false,
                Text = DescribeError(ex),
                Time = FormatTime(errorTime),
                TimeTooltip = FormatTimeTooltip(errorTime),
            });
        }
        finally
        {
            IsSending = false;
        }
    }

    private void AppendMessage(ChatMessageVm message)
    {
        DateOnly day = DateOnly.FromDateTime(DateTime.Now);
        if (_lastDate != day)
        {
            Items.Add(new ChatDateSeparatorVm(day.ToString("yyyy 年 M 月 d 日")));
            _lastDate = day;
        }
        Items.Add(message);
    }

    /// <summary>Turns raw provider/SDK exceptions into a friendlier hint.</summary>
    private static string DescribeError(Exception ex)
    {
        string message = ex.Message ?? string.Empty;
        if (message.Contains("key", StringComparison.OrdinalIgnoreCase))
            return "当前默认模型 Provider 未配置有效的 API Key，请到「设置 · 模型提供方」补全后重试。";
        return $"请求失败：{message}";
    }

    private static string FormatTime(DateTimeOffset time) => time.ToString("HH:mm");

    private static string FormatTimeTooltip(DateTimeOffset time) => time.ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>Collects any reasoning content returned by the model (may be empty).</summary>
    private static string? ExtractReasoning(ChatResponse response)
    {
        StringBuilder builder = new();
        foreach (ChatMessage message in response.Messages)
        {
            foreach (AIContent content in message.Contents)
            {
                if (content is TextReasoningContent reasoning && !string.IsNullOrEmpty(reasoning.Text))
                    builder.Append(reasoning.Text);
            }
        }
        return builder.Length > 0 ? builder.ToString() : null;
    }

    /// <summary>Minimal in-memory session identity required by <see cref="MicroChatContext"/>.</summary>
    private sealed class DesktopChatSession(string id) : IMicroSession
    {
        public string Id { get; } = id;

        public string Title => "桌面会话";
    }
}

/// <summary>A centered date divider row inside the message list.</summary>
public sealed class ChatDateSeparatorVm(string label)
{
    public string Label { get; } = label;
}

/// <summary>A single attachment chip (real picked file, memory only).</summary>
public sealed class ChatAttachmentVm
{
    public string Icon { get; init; } = "📄";

    public string Name { get; init; } = string.Empty;

    public string SizeText { get; init; } = string.Empty;

    public string? FilePath { get; init; }
}

/// <summary>A single chat bubble (user or assistant).</summary>
public sealed partial class ChatMessageVm : ObservableObject
{
    public bool IsFromUser { get; init; }

    public string Text { get; init; } = string.Empty;

    /// <summary>Short 24h label shown under the bubble.</summary>
    public string Time { get; init; } = string.Empty;

    /// <summary>Full timestamp shown on hover.</summary>
    public string TimeTooltip { get; init; } = string.Empty;

    /// <summary>Model reasoning, when the provider returns any.</summary>
    public string? Thinking { get; init; }

    public ObservableCollection<ChatAttachmentVm> Attachments { get; } = [];

    [ObservableProperty]
    private bool _isThinkingExpanded;

    public bool HasThinking => !string.IsNullOrWhiteSpace(Thinking);

    public bool HasText => !string.IsNullOrWhiteSpace(Text);

    public bool ShowAvatar => !IsFromUser;

    public HorizontalAlignment RowAlignment => IsFromUser ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    [RelayCommand]
    private void ToggleThinking() => IsThinkingExpanded = !IsThinkingExpanded;
}
