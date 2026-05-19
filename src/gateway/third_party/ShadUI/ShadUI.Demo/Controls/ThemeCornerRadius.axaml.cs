using Avalonia;
using Avalonia.Controls.Primitives;

namespace ShadUI.Demo.Controls;

public class ThemeCornerRadius : TemplatedControl
{
    public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<ThemeCornerRadius, string>(
        nameof(Title));

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly StyledProperty<double> BoxWidthProperty =
        AvaloniaProperty.Register<ThemeCornerRadius, double>(
            nameof(BoxWidth));

    public double BoxWidth
    {
        get => GetValue(BoxWidthProperty);
        set => SetValue(BoxWidthProperty, value);
    }

    public static readonly StyledProperty<double> BoxHeightProperty =
        AvaloniaProperty.Register<ThemeCornerRadius, double>(
            nameof(BoxHeight));

    public double BoxHeight
    {
        get => GetValue(BoxHeightProperty);
        set => SetValue(BoxHeightProperty, value);
    }
}