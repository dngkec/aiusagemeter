using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AIUsageMeter.Windows.Tests;

/// <summary>
/// Renders a control to pixels so a test can look at what it actually drew.
/// </summary>
/// <remarks>
/// Geometry tests prove the shapes are right; these prove the control puts them on screen in the
/// right colours. WPF visuals need a single-threaded apartment, which the test host does not give
/// us, so each render runs on its own STA thread.
/// </remarks>
internal static class Rendering
{
    /// <summary>Runs <paramref name="work"/> on a fresh STA thread and returns its result.</summary>
    public static T Sta<T>(Func<T> work)
    {
        T? result = default;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("STA work failed", failure);
        return result!;
    }

    /// <summary>Lays out and rasterises an element at the given size, on a black ground.</summary>
    public static Probe Render(FrameworkElement element, double width, double height, double dpiScale = 1,
        bool blackGround = true)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();

        var pixelWidth = (int)Math.Ceiling(width * dpiScale);
        var pixelHeight = (int)Math.Ceiling(height * dpiScale);
        var target = new RenderTargetBitmap(pixelWidth, pixelHeight, 96 * dpiScale, 96 * dpiScale, PixelFormats.Pbgra32);

        // The overlay always sits on its own black surface, so match that rather than leaving the
        // ground transparent and comparing against premultiplied nothing.
        if (blackGround)
        {
            var ground = new DrawingVisual();
            using (var context = ground.RenderOpen())
                context.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height));
            target.Render(ground);
        }

        target.Render(element);

        var stride = pixelWidth * 4;
        var pixels = new byte[stride * pixelHeight];
        target.CopyPixels(pixels, stride, 0);
        return new Probe(pixels, pixelWidth, pixelHeight, stride, dpiScale);
    }

    /// <summary>A rasterised element, addressed in the element's own coordinates.</summary>
    internal sealed class Probe(byte[] pixels, int width, int height, int stride, double dpiScale)
    {
        public int Width => width;
        public int Height => height;

        public Color At(double x, double y)
        {
            var px = Math.Clamp((int)Math.Round(x * dpiScale), 0, width - 1);
            var py = Math.Clamp((int)Math.Round(y * dpiScale), 0, height - 1);
            var offset = py * stride + px * 4;
            return Color.FromArgb(pixels[offset + 3], pixels[offset + 2], pixels[offset + 1], pixels[offset]);
        }

        /// <summary>The colour at a polar offset from the element's centre.</summary>
        public Color AtPolar(double radius, double degrees)
        {
            var angle = degrees * Math.PI / 180;
            var centreX = width / (2 * dpiScale);
            var centreY = height / (2 * dpiScale);
            return At(centreX + radius * Math.Cos(angle), centreY + radius * Math.Sin(angle));
        }

        /// <summary>Coverage at a point, for telling a black surface apart from no surface at all.</summary>
        public byte AlphaAt(double x, double y) => At(x, y).A;

        /// <summary>How many pixels differ from black by more than a hair.</summary>
        public int InkedPixels()
        {
            var count = 0;
            for (var offset = 0; offset < pixels.Length; offset += 4)
                if (pixels[offset] > 8 || pixels[offset + 1] > 8 || pixels[offset + 2] > 8)
                    count++;
            return count;
        }

        public static double Distance(Color left, Color right)
            => Math.Sqrt(Math.Pow(left.R - right.R, 2) + Math.Pow(left.G - right.G, 2) + Math.Pow(left.B - right.B, 2));

        /// <summary>True when two colours are within a tolerance that survives antialiasing.</summary>
        public static bool Close(Color left, Color right, double tolerance = 24)
            => Distance(left, right) <= tolerance;
    }
}
