using System;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using MicroClaw.Desktop.ViewModels;

namespace MicroClaw.Desktop.Views;

public partial class ScenarioRadarView : UserControl
{
    private const double CenterX = 120;
    private const double CenterY = 130;
    private const double Radius = 88;

    private System.Collections.ObjectModel.ObservableCollection<ScenarioScoreItemVm>? _bound;

    public ScenarioRadarView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChangedHandler;
        ActualThemeVariantChanged += (_, _) => Redraw();
        AttachedToVisualTree += (_, _) => Redraw();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnDataContextChangedHandler(object? sender, EventArgs e)
    {
        DetachFromBound();
        if (DataContext is ProviderDetailVm vm)
        {
            _bound = vm.ScenarioScores;
            _bound.CollectionChanged += OnScoresCollectionChanged;
            foreach (var item in _bound) item.PropertyChanged += OnItemPropertyChanged;
        }
        Redraw();
    }

    private void DetachFromBound()
    {
        if (_bound is null) return;
        _bound.CollectionChanged -= OnScoresCollectionChanged;
        foreach (var item in _bound) item.PropertyChanged -= OnItemPropertyChanged;
        _bound = null;
    }

    private void OnScoresCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (ScenarioScoreItemVm o in e.OldItems) o.PropertyChanged -= OnItemPropertyChanged;
        if (e.NewItems is not null)
            foreach (ScenarioScoreItemVm n in e.NewItems) n.PropertyChanged += OnItemPropertyChanged;
        Redraw();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScenarioScoreItemVm.Score)) Redraw();
    }

    private void Redraw()
    {
        var canvas = this.FindControl<Canvas>("DrawCanvas");
        if (canvas is null) return;
        canvas.Children.Clear();

        if (_bound is null || _bound.Count == 0) return;
        int n = _bound.Count;

        // Theme-aware brushes — resolved from ShadUI dynamic resources so both
        // light/dark variants render correctly.
        var gridStroke = ResolveBrush("BorderColor30", Color.FromArgb(0x4C, 0xCE, 0xCE, 0xCE));
        var axisStroke = ResolveBrush("BorderColor", Color.FromRgb(0xCE, 0xCE, 0xCE));
        var labelBrush = ResolveBrush("MutedColor", Color.FromRgb(0x71, 0x71, 0x7A));
        var fgColor = ResolveColor("ForegroundColor", Color.FromRgb(0x18, 0x18, 0x1B));
        var fillBrush = new SolidColorBrush(fgColor) { Opacity = 0.18 };
        var strokeBrush = new SolidColorBrush(fgColor);
        var pointBrush = strokeBrush;

        // Background rings (25/50/75/100 %)
        for (int level = 1; level <= 4; level++)
        {
            var ringPoints = new Points();
            double rr = Radius * level / 4.0;
            for (int i = 0; i < n; i++)
            {
                var (rad, _, _) = AngleFor(i, n);
                ringPoints.Add(new Point(CenterX + rr * Math.Cos(rad), CenterY + rr * Math.Sin(rad)));
            }
            canvas.Children.Add(new Polygon
            {
                Points = ringPoints,
                Stroke = gridStroke,
                StrokeThickness = 1,
                Fill = Brushes.Transparent,
            });
        }

        // Axes
        for (int i = 0; i < n; i++)
        {
            var (rad, _, _) = AngleFor(i, n);
            canvas.Children.Add(new Line
            {
                StartPoint = new Point(CenterX, CenterY),
                EndPoint = new Point(CenterX + Radius * Math.Cos(rad), CenterY + Radius * Math.Sin(rad)),
                Stroke = axisStroke,
                StrokeThickness = 1,
            });
        }

        // Data polygon
        var dataPts = new Points();
        for (int i = 0; i < n; i++)
        {
            var (rad, _, _) = AngleFor(i, n);
            double s = Math.Clamp(_bound[i].Score, 0, 100) / 100.0;
            dataPts.Add(new Point(CenterX + Radius * s * Math.Cos(rad), CenterY + Radius * s * Math.Sin(rad)));
        }
        canvas.Children.Add(new Polygon
        {
            Points = dataPts,
            Fill = fillBrush,
            Stroke = strokeBrush,
            StrokeThickness = 1.5,
        });

        // Points + labels
        for (int i = 0; i < n; i++)
        {
            var (rad, _, _) = AngleFor(i, n);
            double s = Math.Clamp(_bound[i].Score, 0, 100) / 100.0;
            double px = CenterX + Radius * s * Math.Cos(rad);
            double py = CenterY + Radius * s * Math.Sin(rad);
            var dot = new Ellipse { Width = 6, Height = 6, Fill = pointBrush };
            Canvas.SetLeft(dot, px - 3);
            Canvas.SetTop(dot, py - 3);
            canvas.Children.Add(dot);

            // Label
            double lx = CenterX + (Radius + 14) * Math.Cos(rad);
            double ly = CenterY + (Radius + 14) * Math.Sin(rad);
            var label = new TextBlock
            {
                Text = ShortLabel(_bound[i].Label),
                FontSize = 11,
                Foreground = labelBrush,
            };
            // Center the label text around (lx, ly): approximate
            double estW = label.Text!.Length * 11 * 0.9;
            Canvas.SetLeft(label, lx - estW / 2);
            Canvas.SetTop(label, ly - 8);
            canvas.Children.Add(label);
        }
    }

    /// <summary>Returns (angleRad, cos, sin) for axis i, starting at top and rotating clockwise.</summary>
    private static (double rad, double cos, double sin) AngleFor(int i, int total)
    {
        double rad = -Math.PI / 2 + 2 * Math.PI * i / total;
        return (rad, Math.Cos(rad), Math.Sin(rad));
    }

    /// <summary>取 Description 中文首段，避免标签过长。</summary>
    private static string ShortLabel(string label)
    {
        if (string.IsNullOrEmpty(label)) return string.Empty;
        int sep = label.IndexOfAny([' ', '/', '·']);
        return sep > 0 ? label[..sep] : label;
    }

    private Color ResolveColor(string key, Color fallback)
    {
        if (this.TryFindResource(key, ActualThemeVariant, out var v) && v is Color c) return c;
        return fallback;
    }

    private IBrush ResolveBrush(string key, Color fallback) =>
        new SolidColorBrush(ResolveColor(key, fallback));
}
