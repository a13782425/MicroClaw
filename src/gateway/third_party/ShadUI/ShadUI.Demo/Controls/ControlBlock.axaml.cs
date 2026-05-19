using Avalonia;
using Avalonia.Controls;

namespace ShadUI.Demo.Controls;

public class ControlBlock : ContentControl
{
    public static readonly StyledProperty<string?> XamlProperty =
        AvaloniaProperty.Register<CodeTextBlock, string?>(nameof(Xaml));

    public string? Xaml
    {
        get => GetValue(XamlProperty);
        set => SetValue(XamlProperty, value);
    }

    public static readonly StyledProperty<string> CSharpProperty = AvaloniaProperty.Register<ControlBlock, string>(
        nameof(CSharp));

    public string CSharp
    {
        get => GetValue(CSharpProperty);
        set => SetValue(CSharpProperty, value);
    }

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<CodeTextBlock, string?>(nameof(Title));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }
}