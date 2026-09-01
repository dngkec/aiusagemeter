using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AIUsageMeter.Windows.Overlay;

namespace AIUsageMeter.Windows.Tests;

/// <summary>
/// Writes a contact sheet of the icon font so code points can be chosen by looking at them.
/// </summary>
/// <remarks>
/// Segoe Fluent Icons ships with its glyph names stripped, so there is no way to look a code point
/// up from the font itself. Skipped by default; set AIUSAGEMETER_ICON_SHEET to a folder to run it.
/// </remarks>
[TestClass]
public sealed class IconSheet
{
    [TestMethod]
    public void WriteContactSheet()
    {
        var folder = Environment.GetEnvironmentVariable("AIUSAGEMETER_ICON_SHEET");
        if (string.IsNullOrEmpty(folder))
        {
            Assert.Inconclusive("Set AIUSAGEMETER_ICON_SHEET to a folder to regenerate the sheet.");
            return;
        }

        var start = int.Parse(Environment.GetEnvironmentVariable("AIUSAGEMETER_ICON_START") ?? "E700",
            NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        const int columns = 16;
        const int rows = 16;
        const int cell = 64;
        const int label = 16;

        var path = Rendering.Sta(() =>
        {
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawRectangle(Brushes.Black, null, new Rect(0, 0, columns * cell, rows * (cell + label)));
                for (var index = 0; index < columns * rows; index++)
                {
                    var codepoint = start + index;
                    var x = index % columns * cell;
                    var y = index / columns * (cell + label);

                    var glyph = new FormattedText(char.ConvertFromUtf32(codepoint), CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        new Typeface(GlyphRenderer.IconFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                        34, Brushes.White, 1) { TextAlignment = TextAlignment.Center, MaxTextWidth = cell };
                    context.DrawText(glyph, new Point(x, y + 6));

                    var caption = new FormattedText(codepoint.ToString("X4"), CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Consolas"), 11, Brushes.Gray, 1)
                    { TextAlignment = TextAlignment.Center, MaxTextWidth = cell };
                    context.DrawText(caption, new Point(x, y + cell - 4));
                }
            }

            var target = new RenderTargetBitmap(columns * cell, rows * (cell + label), 96, 96, PixelFormats.Pbgra32);
            target.Render(visual);

            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, $"icons-{start:X4}.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(target));
            using var stream = File.Create(file);
            encoder.Save(stream);
            return file;
        });

        Assert.IsTrue(File.Exists(path));
    }
}

/// <summary>Renders every provider's mark so they can be looked at side by side.</summary>
[TestClass]
public sealed class GlyphSheet
{
    [TestMethod]
    public void WriteProviderMarks()
    {
        var folder = Environment.GetEnvironmentVariable("AIUSAGEMETER_ICON_SHEET");
        if (string.IsNullOrEmpty(folder))
        {
            Assert.Inconclusive("Set AIUSAGEMETER_ICON_SHEET to a folder to regenerate the sheet.");
            return;
        }

        var ids = AIUsageMeter.Core.ProviderInfo.All;
        const int columns = 7;
        const int cell = 92;
        const int label = 18;
        var rows = (ids.Length + columns - 1) / columns;

        var path = Rendering.Sta(() =>
        {
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawRectangle(Brushes.Black, null, new Rect(0, 0, columns * cell, rows * (cell + label)));
                for (var index = 0; index < ids.Length; index++)
                {
                    var x = index % columns * cell;
                    var y = index / columns * (cell + label);
                    var box = new Rect(x + 20, y + 12, cell - 40, cell - 40);

                    GlyphRenderer.Draw(context, AIUsageMeter.Windows.Design.ProviderGlyphs.For(ids[index]),
                        box, Brushes.White, 1);

                    var caption = new FormattedText(ids[index].ToString(), CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, new Typeface("Consolas"), 11, Brushes.Gray, 1)
                    { TextAlignment = TextAlignment.Center, MaxTextWidth = cell };
                    context.DrawText(caption, new Point(x, y + cell - 22));
                }
            }

            var target = new RenderTargetBitmap(columns * cell, rows * (cell + label), 96, 96, PixelFormats.Pbgra32);
            target.Render(visual);

            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, "provider-marks.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(target));
            using var stream = File.Create(file);
            encoder.Save(stream);
            return file;
        });

        Assert.IsTrue(File.Exists(path));
    }
}
