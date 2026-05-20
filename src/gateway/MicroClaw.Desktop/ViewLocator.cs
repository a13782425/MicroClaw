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
            case HomeViewModel homeViewModel:
                return new HomeView { DataContext = homeViewModel };
            case InboxViewModel inboxViewModel:
                return new InboxView { DataContext = inboxViewModel };
            case SearchViewModel searchViewModel:
                return new SearchView { DataContext = searchViewModel };
            case WorkflowViewModel workflowViewModel:
                return new WorkflowView { DataContext = workflowViewModel };
            default:
                return new TextBlock { Text = "View not found: " + param.GetType().FullName };
        }
        
    }
    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}