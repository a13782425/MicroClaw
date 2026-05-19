using Avalonia;
using Avalonia.Controls.Primitives;

namespace ShadUI.Demo.Controls;

public class ThemeColor : TemplatedControl
{
    public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<ThemeColor, string>(
        nameof(Title));

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }
}