using Avalonia.Controls;
using Avalonia.Controls.Converters;
using Avalonia.Controls.Primitives.Converters;
using Avalonia.Data.Converters;
using Avalonia.Media;

// ReSharper disable once CheckNamespace
namespace ShadUI;

/// <summary>
///     Provides wrapper access to common Avalonia converters for use in XAML.
/// </summary>
/// <remarks>
///     This class provides static properties that expose commonly used Avalonia converters
///     for easy access in XAML bindings.
/// </remarks>
public static class BasicConverters
{
    /// <summary>
    ///     Gets the EnumToBoolConverter instance for converting enum values to boolean.
    /// </summary>
    public static EnumToBoolConverter EnumToBoolConverter { get; } = new();

    /// <summary>
    ///     Gets the ToBrushConverter instance for converting values to brushes.
    /// </summary>
    public static ToBrushConverter ToBrushConverter { get; } = new();

    /// <summary>
    ///     Gets the DoNothingForNullConverter instance for handling null values.
    /// </summary>
    public static DoNothingForNullConverter DoNothingForNullConverter { get; } = new();

    /// <summary>
    ///     Gets the ColorToDisplayNameConverter instance for converting colors to display names.
    /// </summary>
    public static ColorToDisplayNameConverter ColorToDisplayNameConverter { get; } = new();

    /// <summary>
    ///     Gets the ContrastBrushConverter instance for generating contrasting brushes.
    /// </summary>
    public static ContrastBrushConverter ContrastBrushConverter { get; } = new();

    /// <summary>
    ///     Gets the AccentColorConverter instance for converting accent colors.
    /// </summary>
    public static AccentColorConverter AccentColorConverter { get; } = new();

    /// <summary>
    ///     Converts a <see cref="Color" /> to its hex string representation.
    /// </summary>
    public static readonly IValueConverter ToColorStringConverter =
        new FuncValueConverter<Color, string, string>((color, param) =>
        {
            var toUpper = param is "ToUpper";
            var hex = $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
            return toUpper ? hex.ToUpper() : hex;
        });

    /// <summary>
    ///     Converts a <see cref="Color" /> to a solid <see cref="IBrush" />.
    /// </summary>
    public static readonly IValueConverter ToColorBrushConverter =
        new FuncValueConverter<Color, IBrush?>(color => new SolidColorBrush(color));
}