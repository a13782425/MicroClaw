using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MicroClaw.Desktop;

public partial class SessionViewModel : ViewModelBase
{
    public const string DefaultSessionId = "default";
    public const string RouteTabChat = "chat";
    public const string RouteTabGame = "game";

    public SessionViewModel(
        string sessionId,
        string title,
        string subtitle,
        string workspaceName,
        string worktreeName,
        string branchName)
    {
        SessionId = sessionId;
        Title = title;
        _chatTab = new SessionChatTabViewModel(title, subtitle, workspaceName, worktreeName, branchName);
        _gameTab = new SessionGameTabViewModel();
        NavigateTab(RouteTabChat);
    }

    private readonly SessionChatTabViewModel _chatTab;
    private readonly SessionGameTabViewModel _gameTab;

    public string SessionId { get; }

    public string Title { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChatTabActive))]
    [NotifyPropertyChangedFor(nameof(IsGameTabActive))]
    private string currentTabRoute = RouteTabChat;

    [ObservableProperty]
    private ObservableObject? currentTabPage;

    public bool IsChatTabActive => CurrentTabRoute == RouteTabChat;

    public bool IsGameTabActive => CurrentTabRoute == RouteTabGame;

    [RelayCommand]
    private void NavigateTab(string route)
    {
        CurrentTabRoute = route;
        CurrentTabPage = route switch
        {
            RouteTabGame => (ObservableObject)_gameTab,
            _ => _chatTab,
        };
    }

    public static SessionViewModel CreateDefault()
    {
        return new SessionViewModel(
            DefaultSessionId,
            "默认会话",
            "桌面壳预览 · 当前只接静态路由",
            "MicroClaw 工作区",
            "desktop-shell-preview",
            "main");
    }

    public static SessionViewModel CreatePreview(string sessionId)
    {
        return new SessionViewModel(
            sessionId,
            $"会话 {sessionId}",
            "会话模板已接入，内容仍为静态占位",
            "临时上下文",
            "未连接到真实工作区",
            "preview");
    }
}