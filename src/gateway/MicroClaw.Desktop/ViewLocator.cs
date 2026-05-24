using Avalonia.Controls;
using Avalonia.Controls.Templates;
using MicroClaw.Desktop.Views;

namespace MicroClaw.Desktop;

public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
        {
            return new TextBlock { Text = "View not found: Null" };
        }

        switch (param)
        {
            case SessionViewModel vm:
                return new SessionView { DataContext = vm };
            case SessionChatTabViewModel vm:
                return new SessionChatTabView { DataContext = vm };
            case SessionGameTabViewModel vm:
                return new SessionGameTabView { DataContext = vm };
            case UsageViewModel vm:
                return new UsageView { DataContext = vm };
            case MicroAgentsViewModel vm:
                return new MicroAgentsView { DataContext = vm };
            case MicroSkillsViewModel vm:
                return new MicroSkillsView { DataContext = vm };
            case MicroMcpViewModel vm:
                return new MicroMcpView { DataContext = vm };
            case MicroToolsViewModel vm:
                return new MicroToolsView { DataContext = vm };
            case MicroPluginsViewModel vm:
                return new MicroPluginsView { DataContext = vm };
            case ProvidersViewModel vm:
                return new ProvidersView { DataContext = vm };
            case AboutViewModel vm:
                return new AboutView { DataContext = vm };
            default:
                return new TextBlock { Text = "View not found: " + param.GetType().FullName };
        }
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
