using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EmeraldVeil.App;

internal sealed class VeilSurface : FrameworkElement
{
    private const string BackgroundResourceUri =
        "pack://application:,,,/Assets/emerald-veil-background.jpg";
    private static readonly BitmapSource BackgroundImage = LoadBackgroundImage();

    internal VeilSurface()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = false;
        UseLayoutRounding = false;
    }

    internal void StartAnimation() => InvalidateVisual();

    internal void StopAnimation()
    {
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

        drawingContext.DrawImage(
            BackgroundImage,
            new Rect(0, 0, width, height));
    }

    private static BitmapSource LoadBackgroundImage()
    {
        var resource = System.Windows.Application.GetResourceStream(
            new Uri(BackgroundResourceUri, UriKind.Absolute))
            ?? throw new InvalidOperationException(
                $"The Emerald Veil background resource is missing: {BackgroundResourceUri}");

        using (resource.Stream)
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = resource.Stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
    }
}
