using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

// ReSharper disable once CheckNamespace
namespace ShadUI;

/// <summary>
///     Converts the value of the sidebar menu scroller to an opacity mask.
/// </summary>
/// <remarks>
///     This converter is used to create gradient opacity masks for scrollable content areas.
///     It inspects ScrollViewer Offset/Extent/Viewport values and returns an appropriate gradient brush.
/// </remarks>
public class ScrollerToOpacityMask : IMultiValueConverter
{
    private readonly Func<IList<object?>, IBrush?> _func;

    /// <summary>
    ///     Gets the top mask instance for creating fade-out effects at the top of scrollable content.
    /// </summary>
    /// <remarks>
    ///     Expects a single bound value: ScrollViewer.Offset (Vector).
    ///     Returns the top fade brush when scrolled down from the top, otherwise an opaque white brush.
    /// </remarks>
    public static ScrollerToOpacityMask Top { get; } = new(values =>
        values.Count >= 1 && values[0] is Vector offset && offset.Y > 0
            ? TopBrush
            : Brushes.White);

    /// <summary>
    ///     Gets the bottom mask instance for creating fade-out effects at the bottom of scrollable content.
    /// </summary>
    /// <remarks>
    ///     Expects three bound values: ScrollViewer.Offset (Vector), Extent (Size), Viewport (Size).
    ///     Returns the bottom fade brush when not yet at the bottom, otherwise an opaque white brush.
    /// </remarks>
    public static ScrollerToOpacityMask Bottom { get; } = new(values =>
        values.Count >= 3
        && values[0] is Vector offset
        && values[1] is Size extent
        && values[2] is Size viewport
        && offset.Y < extent.Height - viewport.Height
            ? BottomBrush
            : Brushes.White);

    /// <summary>
    ///     The bottom gradient brush that fades from opaque to transparent.
    /// </summary>
    private static readonly LinearGradientBrush BottomBrush = new()
    {
        StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0.5, 0.95, RelativeUnit.Relative),
        GradientStops =
        [
            new GradientStop(Colors.Black, 0.9),
            new GradientStop(Colors.Transparent, 1)
        ]
    };

    /// <summary>
    ///     The top gradient brush that fades from transparent to opaque.
    /// </summary>
    private static readonly LinearGradientBrush TopBrush = new()
    {
        StartPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0.5, 0.05, RelativeUnit.Relative),
        GradientStops =
        [
            new GradientStop(Colors.Black, 0.9),
            new GradientStop(Colors.Transparent, 1)
        ]
    };

    /// <summary>
    ///     Initializes a new instance of the <see cref="ScrollerToOpacityMask" /> class.
    /// </summary>
    /// <param name="func">The function that determines which brush to return based on the bound values.</param>
    public ScrollerToOpacityMask(Func<IList<object?>, IBrush?> func)
    {
        _func = func;
    }

    /// <summary>
    ///     Converts the bound ScrollViewer values to an opacity mask.
    /// </summary>
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        return _func(values);
    }
}