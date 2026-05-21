using CommunityToolkit.Mvvm.ComponentModel;

namespace MicroClaw.Desktop;

public partial class SessionViewModel : ViewModelBase
{
    public const string DefaultSessionId = "default";

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
        Subtitle = subtitle;
        WorkspaceName = workspaceName;
        WorktreeName = worktreeName;
        BranchName = branchName;
    }

    public string SessionId { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public string WorkspaceName { get; }

    public string WorktreeName { get; }

    public string BranchName { get; }

    public string ChatModeLabel { get; } = "交互模式";

    public string ModelLabel { get; } = "Claude Sonnet 4.5";

    public string ReasoningLabel { get; } = "中等深度";

    [ObservableProperty]
    private string draftPrompt = string.Empty;

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