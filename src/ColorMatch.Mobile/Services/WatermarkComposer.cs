using ColorMatch.ColorEngine.ColorConversion;
using SkiaSharp;

namespace ColorMatch.Mobile.Services;

/// <summary>
/// Builds the picture that leaves the app: the player's photo with a "ColorMatch" strip along the bottom edge showing the logo, the target
/// colour, the colour that was found and the score. Only used when sharing outside the app; the stored / in-app photo is never touched.
/// </summary>
public static class WatermarkComposer
{
    private const int MaxEdge = 2048;

    public static Task<string> ComposeAsync(string photoPath, string colorName, string targetHex, string foundHex, int score, double deltaE, string outputPath)
        => Task.Run(async () =>
        {
            using var photo = ImageService.LoadUpright(photoPath, MaxEdge);
            using var logo = await LoadLogoAsync();
            using var surface = SKSurface.Create(new SKImageInfo(photo.Width, photo.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
            var canvas = surface.Canvas;
            canvas.DrawBitmap(photo, 0, 0);
            DrawStrip(canvas, photo.Width, photo.Height, logo, colorName, targetHex, foundHex, score, deltaE);

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 92);
            await using var fs = File.Create(outputPath);
            data.SaveTo(fs);
            return outputPath;
        });

    private static async Task<SKBitmap?> LoadLogoAsync()
    {
        try
        {
            await using var s = await FileSystem.OpenAppPackageFileAsync("watermark_logo.png");
            return SKBitmap.Decode(s);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("watermark logo: " + ex.Message); return null; }
    }

    private static SKColor ToSk(string hex) { var c = Rgb.FromHex(hex); return new SKColor(c.R, c.G, c.B); }

    private static SKTypeface Face(bool bold) => SKTypeface.FromFamilyName("sans-serif", bold ? SKFontStyle.Bold : SKFontStyle.Normal) ?? SKTypeface.Default;

    private static void DrawStrip(SKCanvas canvas, int w, int h, SKBitmap? logo, string colorName, string targetHex, string foundHex, int score, double deltaE)
    {
        var u = w / 1080f;                       // one design unit; the strip looks the same on every photo size
        var pad = 36 * u;
        var stripH = Math.Max(190 * u, 0);
        var top = h - stripH;
        var fadeTop = top - 110 * u;             // soft fade above the strip so it blends into the photo

        using (var shade = new SKPaint())
        {
            shade.Shader = SKShader.CreateLinearGradient(new SKPoint(0, fadeTop), new SKPoint(0, h),
                [new SKColor(0, 0, 0, 0), new SKColor(8, 8, 12, 200), new SKColor(8, 8, 12, 232)], [0f, 0.42f, 1f], SKShaderTileMode.Clamp);
            canvas.DrawRect(0, fadeTop, w, h - fadeTop, shade);
        }

        var cy = top + stripH / 2f + 6 * u;

        // ---- left: logo + name ----
        var logoSize = 84 * u;
        var logoRect = new SKRect(pad, cy - logoSize / 2, pad + logoSize, cy + logoSize / 2);
        if (logo is not null)
        {
            using var lp = new SKPaint { IsAntialias = true };
            canvas.DrawBitmap(logo, logoRect, lp);
        }
        using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var soft = new SKPaint { Color = new SKColor(255, 255, 255, 190), IsAntialias = true };
        using var boldFont = new SKFont(Face(true), 38 * u);
        using var smallFont = new SKFont(Face(false), 25 * u);
        var textX = logoRect.Right + 18 * u;
        canvas.DrawText("ColorMatch", textX, cy - 2 * u, boldFont, white);
        var name = Ellipsize(colorName, smallFont, w * 0.30f);
        canvas.DrawText(name, textX, cy + 34 * u, smallFont, soft);

        // ---- right: score ----
        using var scoreFont = new SKFont(Face(true), 96 * u);
        using var ofFont = new SKFont(Face(false), 28 * u);
        var scoreText = score.ToString();
        var ofText = "/1000";
        var ofW = ofFont.MeasureText(ofText);
        var scoreW = scoreFont.MeasureText(scoreText);
        var right = w - pad;
        canvas.DrawText(ofText, right - ofW, cy + 36 * u, ofFont, soft);
        canvas.DrawText(scoreText, right - ofW - 10 * u - scoreW, cy + 36 * u, scoreFont, white);
        using var deltaFont = new SKFont(Face(false), 24 * u);
        var deltaText = $"ΔE {deltaE.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}";
        canvas.DrawText(deltaText, right - deltaFont.MeasureText(deltaText), cy - 52 * u, deltaFont, soft);

        // ---- centre: target → found ----
        var swatch = 68 * u;
        var centreX = w / 2f + 40 * u;
        DrawSwatch(canvas, centreX - 130 * u, cy - 14 * u, swatch, targetHex, "HEDEF", u);
        DrawSwatch(canvas, centreX + 62 * u, cy - 14 * u, swatch, foundHex, "BULUNAN", u);
        using var arrow = new SKPaint { Color = new SKColor(255, 255, 255, 170), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3.2f * u, StrokeCap = SKStrokeCap.Round };
        var ax = centreX - 16 * u;
        canvas.DrawLine(ax - 12 * u, cy - 14 * u, ax + 12 * u, cy - 14 * u, arrow);
        canvas.DrawLine(ax + 5 * u, cy - 21 * u, ax + 12 * u, cy - 14 * u, arrow);
        canvas.DrawLine(ax + 5 * u, cy - 7 * u, ax + 12 * u, cy - 14 * u, arrow);
    }

    private static void DrawSwatch(SKCanvas canvas, float cx, float cy, float size, string hex, string label, float u)
    {
        using var fill = new SKPaint { Color = ToSk(hex), IsAntialias = true };
        using var ring = new SKPaint { Color = new SKColor(255, 255, 255, 230), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3 * u };
        canvas.DrawCircle(cx, cy, size / 2, fill);
        canvas.DrawCircle(cx, cy, size / 2, ring);
        using var font = new SKFont(Face(true), 21 * u);
        using var light = new SKPaint { Color = new SKColor(255, 255, 255, 215), IsAntialias = true };
        var t = hex.ToUpperInvariant();
        canvas.DrawText(t, cx - font.MeasureText(t) / 2, cy + size / 2 + 30 * u, font, light);
    }

    private static string Ellipsize(string text, SKFont font, float maxWidth)
    {
        if (string.IsNullOrEmpty(text) || font.MeasureText(text) <= maxWidth) return text;
        while (text.Length > 1 && font.MeasureText(text + "…") > maxWidth) text = text[..^1];
        return text + "…";
    }
}
