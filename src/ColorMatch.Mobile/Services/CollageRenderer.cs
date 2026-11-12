using ColorMatch.ColorEngine.ColorConversion;
using ColorMatch.ColorEngine.ColorScoring;
using SkiaSharp;

namespace ColorMatch.Mobile.Services;

/// <summary>Cell rectangles are fractions (x, y, w, h) of the content area.</summary>
public sealed record CollageTemplate(string Name, string Description, (float X, float Y, float W, float H)[] Cells)
{
    public int CellCount => Cells.Length;

    public static readonly IReadOnlyList<CollageTemplate> All =
    [
        new("Izgara", "2 × 2", [(0, 0, .5f, .5f), (.5f, 0, .5f, .5f), (0, .5f, .5f, .5f), (.5f, .5f, .5f, .5f)]),
        new("Manşet", "1 büyük + 2", [(0, 0, 1, .58f), (0, .58f, .5f, .42f), (.5f, .58f, .5f, .42f)]),
        new("Şerit", "3 şerit", [(0, 0, 1, 1f / 3), (0, 1f / 3, 1, 1f / 3), (0, 2f / 3, 1, 1f / 3)]),
        new("Mozaik", "1 uzun + 2", [(0, 0, .5f, 1), (.5f, 0, .5f, .5f), (.5f, .5f, .5f, .5f)]),
    ];
}

public sealed record CollageFormat(string Name, int Width, int Height)
{
    public static readonly CollageFormat Story = new("Story 9:16", 1080, 1920);
    public static readonly CollageFormat Post = new("Gönderi 4:5", 1080, 1350);
    public static readonly IReadOnlyList<CollageFormat> All = [Story, Post];
}

public sealed record CollageSpec(
    CollageTemplate Template,
    CollageFormat Format,
    Rgb Background,
    bool ShowPalette,
    IReadOnlyList<SKBitmap> Photos);

/// <summary>Renders collages with SkiaSharp. Pure function of <see cref="CollageSpec"/>: the on-screen preview and the export use the same code.</summary>
public static class CollageRenderer
{
    public static SKBitmap Render(CollageSpec spec, float scale = 1f)
    {
        if (spec.Photos.Count == 0) throw new ArgumentException("En az bir fotoğraf gerekli.");

        var W = (int)MathF.Round(spec.Format.Width * scale);
        var H = (int)MathF.Round(spec.Format.Height * scale);
        var bitmap = new SKBitmap(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(spec.Background.R, spec.Background.G, spec.Background.B));

        var margin = W * 0.06f;
        var gap = W * 0.024f;
        var radius = W * 0.03f;
        var paletteHeight = spec.ShowPalette ? H * 0.12f : 0f;

        var content = new SKRect(margin - gap / 2, margin - gap / 2 + (spec.ShowPalette ? 0 : 0),
            W - margin + gap / 2, H - margin - paletteHeight + gap / 2);
        var bg = ContrastInk(spec.Background);

        for (var i = 0; i < spec.Template.CellCount; i++)
        {
            var (fx, fy, fw, fh) = spec.Template.Cells[i];
            var cell = new SKRect(
                content.Left + fx * content.Width, content.Top + fy * content.Height,
                content.Left + (fx + fw) * content.Width, content.Top + (fy + fh) * content.Height);
            cell.Inflate(-gap / 2, -gap / 2);
            DrawCover(canvas, spec.Photos[i % spec.Photos.Count], cell, radius);
        }

        if (spec.ShowPalette)
        {
            var colors = PaletteExtractor.FromPhotos(spec.Photos, 5);
            var strip = new SKRect(margin, H - margin - paletteHeight + gap, W - margin, H - margin);
            DrawPalette(canvas, colors, strip, bg, radius);
        }
        return bitmap;
    }

    private static void DrawCover(SKCanvas canvas, SKBitmap photo, SKRect cell, float radius)
    {
        canvas.Save();
        using var clip = new SKPath();
        clip.AddRoundRect(cell, radius, radius);
        canvas.ClipPath(clip, SKClipOperation.Intersect, true);

        var scale = Math.Max(cell.Width / photo.Width, cell.Height / photo.Height);
        var dw = photo.Width * scale; var dh = photo.Height * scale;
        var dest = new SKRect(cell.MidX - dw / 2, cell.MidY - dh / 2, cell.MidX + dw / 2, cell.MidY + dh / 2);
        using var image = SKImage.FromBitmap(photo);
        canvas.DrawImage(image, new SKRect(0, 0, photo.Width, photo.Height), dest,
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), null);
        canvas.Restore();
    }

    private static void DrawPalette(SKCanvas canvas, IReadOnlyList<Rgb> colors, SKRect strip, SKColor ink, float radius)
    {
        if (colors.Count == 0) return;
        var gap = strip.Width * 0.02f;
        var cellW = (strip.Width - gap * (colors.Count - 1)) / colors.Count;
        var swatchH = strip.Height * 0.62f;
        using var typeface = SKTypeface.FromFamilyName(null, SKFontStyle.Bold);
        using var font = new SKFont(typeface, strip.Height * 0.17f);
        using var textPaint = new SKPaint { Color = ink, IsAntialias = true };
        using var fill = new SKPaint { IsAntialias = true };

        for (var i = 0; i < colors.Count; i++)
        {
            var x = strip.Left + i * (cellW + gap);
            var rect = new SKRect(x, strip.Top, x + cellW, strip.Top + swatchH);
            fill.Color = new SKColor(colors[i].R, colors[i].G, colors[i].B);
            canvas.DrawRoundRect(rect, radius * 0.6f, radius * 0.6f, fill);
            canvas.DrawText(colors[i].ToHex(), rect.MidX, strip.Top + swatchH + strip.Height * 0.27f, SKTextAlign.Center, font, textPaint);
        }
    }

    public static SKColor ContrastInk(Rgb bg)
    {
        var lab = ColorConverter.ToLab(bg);
        return lab.L > 60 ? new SKColor(0x1B, 0x1B, 0x1F) : new SKColor(0xF4, 0xF4, 0xF5);
    }
}

/// <summary>Finds a photo's dominant colour by binning pixels in CIELAB (prefers saturated colours over grey).</summary>
public static class PaletteExtractor
{
    public static IReadOnlyList<Rgb> FromPhotos(IReadOnlyList<SKBitmap> photos, int max)
    {
        var result = new List<Rgb>();
        foreach (var photo in photos.DistinctBy(p => p.Handle))
        {
            var c = Dominant(photo);
            if (result.All(r => Ciede2000.Difference(r, c) > 8)) result.Add(c);
            if (result.Count >= max) break;
        }
        return result;
    }

    public static Rgb Dominant(SKBitmap bmp)
    {
        const int grid = 48;
        var bins = new Dictionary<(int, int, int), (double w, double l, double a, double b)>();
        for (var gy = 0; gy < grid; gy++)
            for (var gx = 0; gx < grid; gx++)
            {
                var px = bmp.GetPixel(Math.Min(bmp.Width - 1, gx * bmp.Width / grid), Math.Min(bmp.Height - 1, gy * bmp.Height / grid));
                var lab = ColorConverter.ToLab(new Rgb(px.Red, px.Green, px.Blue));
                var key = ((int)Math.Floor(lab.L / 12), (int)Math.Floor(lab.A / 14), (int)Math.Floor(lab.B / 14));
                var weight = 1.0 + lab.Chroma / 40.0;
                bins.TryGetValue(key, out var acc);
                bins[key] = (acc.w + weight, acc.l + lab.L * weight, acc.a + lab.A * weight, acc.b + lab.B * weight);
            }
        var best = bins.Values.MaxBy(v => v.w);
        return ColorConverter.ToRgb(new Lab(best.l / best.w, best.a / best.w, best.b / best.w));
    }
}
