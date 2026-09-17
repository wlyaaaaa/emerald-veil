using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EmeraldVeil.App;

internal sealed class VeilSurface : FrameworkElement
{
    private BitmapSource? _image;
    private Brush _color = Brushes.Black;
    private int _position = 4;
    internal VeilSurface() { IsHitTestVisible = false; }
    internal void Load(VddBackground source)
    {
        _image = null;
        _color = new SolidColorBrush(Color.FromRgb((byte)source.Color, (byte)(source.Color >> 8), (byte)(source.Color >> 16)));
        _position = source.Position;
        if (!string.IsNullOrWhiteSpace(source.Image))
        {
            using var stream = new FileStream(source.Image, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream; image.EndInit(); image.Freeze(); _image = image;
        }
        InvalidateVisual();
    }
    internal void StartAnimation() => InvalidateVisual();
    internal void StopAnimation() { }
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        drawing.PushClip(new RectangleGeometry(bounds));
        drawing.DrawRectangle(_color, null, bounds);
        if (_image is { } image)
        {
            if (_position == 1)
            {
                var brush = new ImageBrush(image) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute,
                    Viewport = new Rect(0, 0, image.Width, image.Height) };
                drawing.DrawRectangle(brush, null, bounds);
            }
            else
            {
                double scale = _position == 0 ? 1 : _position == 3 ?
                    Math.Min(bounds.Width / image.Width, bounds.Height / image.Height) :
                    Math.Max(bounds.Width / image.Width, bounds.Height / image.Height);
                var target = _position == 2 ? bounds : new Rect((bounds.Width-image.Width*scale)/2,
                    (bounds.Height-image.Height*scale)/2, image.Width*scale, image.Height*scale);
                drawing.DrawImage(image, target);
            }
        }
        drawing.Pop();
    }
}