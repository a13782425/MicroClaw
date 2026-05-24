using CommunityToolkit.Mvvm.ComponentModel;

namespace MicroClaw.Desktop;

public partial class SessionChatTabViewModel : ViewModelBase
{
    public SessionChatTabViewModel(
        string title,
        string subtitle,
        string workspaceName,
        string worktreeName,
        string branchName)
    {
        Title = title;
        Subtitle = subtitle;
        WorkspaceName = workspaceName;
        WorktreeName = worktreeName;
        BranchName = branchName;
    }

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
}
