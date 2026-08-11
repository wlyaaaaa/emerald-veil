using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace EmeraldVeil.App;

using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using WpfPoint = System.Windows.Point;

internal sealed class VeilSurface : FrameworkElement
{
    private static readonly MediaBrush BaseDimBrush = Freeze(
        new SolidColorBrush(MediaColor.FromArgb(66, 0, 0, 0)));

    private static readonly MediaBrush[] DriftBrushes =
    [
        CreateDriftBrush(82, 0, 18, 7),
        CreateDriftBrush(70, 0, 12, 5),
        CreateDriftBrush(58, 0, 22, 9),
        CreateDriftBrush(64, 0, 15, 6),
    ];

    private readonly DispatcherTimer _renderTimer;
    private readonly Stopwatch _clock = new();

    internal VeilSurface()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = false;
        UseLayoutRounding = false;

        _renderTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(66),
            DispatcherPriority.Background,
            (_, _) => InvalidateVisual(),
            Dispatcher);
        _renderTimer.Stop();
    }

    internal void StartAnimation()
    {
        if (!_clock.IsRunning)
        {
            _clock.Start();
        }

        _renderTimer.Start();
        InvalidateVisual();
    }

    internal void StopAnimation()
    {
        _renderTimer.Stop();
        _clock.Stop();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        drawingContext.DrawRectangle(BaseDimBrush, null, new Rect(0, 0, width, height));

        var seconds = _clock.Elapsed.TotalSeconds;
        DrawDrift(
            drawingContext,
            DriftBrushes[0],
            width * (0.12 + 0.88 * Oscillate(seconds * 0.013, 0.1)),
            height * (0.20 + 0.64 * Oscillate(seconds * 0.017, 1.7)),
            width * 0.42,
            height * 0.58);
        DrawDrift(
            drawingContext,
            DriftBrushes[1],
            width * (0.08 + 0.84 * Oscillate(seconds * 0.011, 2.3)),
            height * (0.14 + 0.72 * Oscillate(seconds * 0.019, 4.1)),
            width * 0.34,
            height * 0.48);
        DrawDrift(
            drawingContext,
            DriftBrushes[2],
            width * (0.16 + 0.70 * Oscillate(seconds * 0.015, 5.2)),
            height * (0.10 + 0.80 * Oscillate(seconds * 0.009, 0.8)),
            width * 0.50,
            height * 0.35);
        DrawDrift(
            drawingContext,
            DriftBrushes[3],
            width * (0.10 + 0.82 * Oscillate(seconds * 0.008, 3.4)),
            height * (0.18 + 0.64 * Oscillate(seconds * 0.014, 2.0)),
            width * 0.28,
            height * 0.68);
    }

    private static void DrawDrift(
        DrawingContext drawingContext,
        MediaBrush brush,
        double x,
        double y,
        double radiusX,
        double radiusY) =>
        drawingContext.DrawEllipse(brush, null, new WpfPoint(x, y), radiusX, radiusY);

    private static double Oscillate(double phase, double offset) =>
        0.5 + 0.5 * Math.Sin((phase * Math.Tau) + offset);

    private static MediaBrush CreateDriftBrush(byte alpha, byte red, byte green, byte blue)
    {
        var brush = new RadialGradientBrush
        {
            Center = new WpfPoint(0.5, 0.5),
            GradientOrigin = new WpfPoint(0.44, 0.42),
            RadiusX = 0.5,
            RadiusY = 0.5,
        };
        brush.GradientStops.Add(new GradientStop(MediaColor.FromArgb(alpha, red, green, blue), 0));
        brush.GradientStops.Add(new GradientStop(MediaColor.FromArgb((byte)(alpha / 2), 0, 8, 3), 0.55));
        brush.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        return Freeze(brush);
    }

    private static T Freeze<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
