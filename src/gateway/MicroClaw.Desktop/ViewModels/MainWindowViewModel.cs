using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MicroClaw.Desktop;

public partial class MainWindowViewModel : ObservableObject
{
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(ActiveThemeLabel))]
	[NotifyPropertyChangedFor(nameof(ThemeIcon))]
	[NotifyPropertyChangedFor(nameof(ThemeButtonToolTip))]
	private ThemeOption? selectedTheme;

	[ObservableProperty]
	private string draftPrompt = string.Empty;

	public MainWindowViewModel()
	{
		SelectedTheme = ThemeOptions[0];
	}

	public IReadOnlyList<SessionPreview> Sessions { get; } =
	[
		new("Quick chats", "", false),
		new("microclaw-desktop", "visual-spike", false),
		new("feat: borderless shell", "in-progress", true),
		new("agent-runtime", "preview", false),
		new("workflow-lab", "draft", false),
		new("rag-memory", "notes", false)
	];

	public IReadOnlyList<RadarItem> RadarItems { get; } =
	[
		new("Enable borderless desktop shell", "#454", "microclaw/microclaw · ui-preview · 2h ago", true),
		new("Address prompt card spacing", "#845", "microclaw/microclaw · desktop · 4h ago", false),
		new("Introduce theme toggle feedback", "#423", "microclaw/microclaw · shadui · 1d ago", true),
		new("feat: minimal session sidebar", "#363", "microclaw/microclaw · worktree · 2d ago", false)
	];

	public IReadOnlyList<ThemeOption> ThemeOptions { get; } =
	[
		new("跟随系统", "使用操作系统主题", ThemeVariant.Default),
		new("浅色", "明亮工作台预览", ThemeVariant.Light),
		new("深色", "低亮度工作台预览", ThemeVariant.Dark)
	];

	public string ActiveThemeLabel => SelectedTheme?.DisplayName ?? "跟随系统";

	public string ThemeIcon
	{
		get
		{
			if (SelectedTheme?.Variant == ThemeVariant.Light)
			{
				return "\uE2B1";
			}

			if (SelectedTheme?.Variant == ThemeVariant.Dark)
			{
				return "\uE122";
			}

			return "\uE2B2";
		}
	}

	public string ThemeButtonToolTip => $"主题：{ActiveThemeLabel}";

	[RelayCommand]
	private void CycleTheme()
	{
		var currentIndex = GetSelectedThemeIndex();
		var nextIndex = currentIndex < 0 || currentIndex + 1 >= ThemeOptions.Count ? 0 : currentIndex + 1;
		SelectedTheme = ThemeOptions[nextIndex];
	}

	private int GetSelectedThemeIndex()
	{
		for (var index = 0; index < ThemeOptions.Count; index++)
		{
			if (ThemeOptions[index] == SelectedTheme)
			{
				return index;
			}
		}

		return -1;
	}

	partial void OnSelectedThemeChanged(ThemeOption? value)
	{
		if (value is null || Application.Current is not { } app)
		{
			return;
		}

		app.RequestedThemeVariant = value.Variant;
	}

}

public sealed record SessionPreview(string Title, string Meta, bool IsActive);

public sealed record RadarItem(string Title, string Number, string Meta, bool IsCompleted);

public sealed record ThemeOption(string DisplayName, string Description, ThemeVariant Variant)
{
	public override string ToString() => DisplayName;
}