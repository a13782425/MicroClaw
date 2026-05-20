using CommunityToolkit.Mvvm.ComponentModel;

namespace MicroClaw.Desktop;

public partial class HomeViewModel : ViewModelBase
{
	[ObservableProperty]
	private string draftPrompt = string.Empty;
}
