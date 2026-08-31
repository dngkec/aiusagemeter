namespace AIUsageMeter.Core;

public readonly record struct MeterRect(double X, double Y, double Width, double Height);

public static class OverlayLayout
{
    public static MeterRect Place(MeterRect workArea, double width, double height, VerticalPosition position, double offset)
    {
        var baseY = position switch
        {
            VerticalPosition.Top => workArea.Y + 24,
            VerticalPosition.Bottom => workArea.Y + workArea.Height - height - 24,
            _ => workArea.Y + (workArea.Height - height) / 2
        };
        var y = Math.Clamp(baseY + offset, workArea.Y, workArea.Y + Math.Max(0, workArea.Height - height));
        return new(workArea.X + Math.Max(0, workArea.Width - width), y, width, Math.Min(height, workArea.Height));
    }
}

public static class SupportLinks
{
    public static readonly Uri Repository = new("https://github.com/dngkec/aiusagemeter");
    public static readonly Uri Issues = new("https://github.com/dngkec/aiusagemeter/issues");
    public static readonly Uri Sponsor = new("https://buymeacoffee.com/dngkec");
    public static readonly Uri Designer = new("https://x.com/hivinz_");
    public static bool IsAllowed(Uri uri) => new[] { Repository, Issues, Sponsor, Designer }.Any(x => x == uri);
}
