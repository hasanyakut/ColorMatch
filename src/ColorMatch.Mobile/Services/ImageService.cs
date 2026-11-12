using ColorMatch.ColorEngine.ColorAnalysis;
using ColorMatch.ColorEngine.ColorConversion;
using ColorMatch.ColorEngine.ColorScoring;
using SkiaSharp;

namespace ColorMatch.Mobile.Services;

public sealed record PreparedPhoto(string Path, int Width, int Height);

public sealed record ColorPreview(Rgb Found, double DeltaE, int Score, int UsedPixels, double Share = 1);

/// <summary>Skia based image helpers: upright normalisation, region sampling and the instant on-device preview.</summary>
public sealed class ImageService
{
    public const int UploadMaxEdge = 1600;
    private const int MaxSamples = 40_000;

    private static string CacheDir
    {
        get
        {
            var dir = System.IO.Path.Combine(FileSystem.CacheDirectory, "photos");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public string NewCachePath(string extension = ".jpg") => System.IO.Path.Combine(CacheDir, Guid.NewGuid().ToString("N") + extension);

    /// <summary>
    /// Decodes <paramref name="sourcePath"/>, applies the EXIF orientation, downsizes and writes an upright JPEG to the cache.
    /// The server uses the same convention, so normalised (0..1) coordinates picked on this image are valid there.
    /// </summary>
    public Task<PreparedPhoto> PrepareAsync(string sourcePath, int maxEdge = UploadMaxEdge, CancellationToken ct = default)
        => Task.Run(() =>
        {
            using var bmp = LoadUpright(sourcePath, maxEdge);
            var output = NewCachePath();
            using (var image = SKImage.FromBitmap(bmp))
            using (var data = image.Encode(SKEncodedImageFormat.Jpeg, 90))
            using (var fs = File.Create(output))
                data.SaveTo(fs);
            return new PreparedPhoto(output, bmp.Width, bmp.Height);
        }, ct);

    /// <summary>Loads an upright bitmap whose longest edge is at most <paramref name="maxEdge"/>.</summary>
    public static SKBitmap LoadUpright(string path, int maxEdge)
    {
        using var stream = File.OpenRead(path);
        using var codec = SKCodec.Create(stream) ?? throw new InvalidOperationException("Görsel okunamadı.");
        using var src = SKBitmap.Decode(codec) ?? throw new InvalidOperationException("Görsel okunamadı.");

        var origin = codec.EncodedOrigin;
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        int w = src.Width, h = src.Height;
        int ow = swap ? h : w, oh = swap ? w : h;
        var scale = Math.Min(1.0, maxEdge / (double)Math.Max(ow, oh));
        var tw = Math.Max(1, (int)Math.Round(ow * scale));
        var th = Math.Max(1, (int)Math.Round(oh * scale));

        var target = new SKBitmap(new SKImageInfo(tw, th, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(target);
        canvas.Clear(SKColors.White);
        canvas.SetMatrix(OrientationMatrix(origin, w, h, (float)scale));
        using var image = SKImage.FromBitmap(src);
        canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), null);
        return target;
    }

    private static SKMatrix OrientationMatrix(SKEncodedOrigin origin, int w, int h, float s)
    {
        float a, b, tx, c, d, ty;
        switch (origin)
        {
            case SKEncodedOrigin.TopRight:    (a, b, tx, c, d, ty) = (-1, 0, w, 0, 1, 0); break;
            case SKEncodedOrigin.BottomRight: (a, b, tx, c, d, ty) = (-1, 0, w, 0, -1, h); break;
            case SKEncodedOrigin.BottomLeft:  (a, b, tx, c, d, ty) = (1, 0, 0, 0, -1, h); break;
            case SKEncodedOrigin.LeftTop:     (a, b, tx, c, d, ty) = (0, 1, 0, 1, 0, 0); break;
            case SKEncodedOrigin.RightTop:    (a, b, tx, c, d, ty) = (0, -1, h, 1, 0, 0); break;
            case SKEncodedOrigin.RightBottom: (a, b, tx, c, d, ty) = (0, -1, h, -1, 0, w); break;
            case SKEncodedOrigin.LeftBottom:  (a, b, tx, c, d, ty) = (0, 1, 0, -1, 0, w); break;
            default:                          (a, b, tx, c, d, ty) = (1, 0, 0, 0, 1, 0); break;
        }
        return new SKMatrix(a * s, b * s, tx * s, c * s, d * s, ty * s, 0, 0, 1);
    }

    /// <summary>Holds a decoded copy of a photo for fast repeated sampling while the user moves the selector.</summary>
    public static PhotoSampler OpenSampler(string path) => new(LoadUpright(path, 1024));

    public static Task<string> SaveJpegAsync(SKBitmap bitmap, string path, int quality = 92)
        => Task.Run(() =>
        {
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
            using var fs = File.Create(path);
            data.SaveTo(fs);
            return path;
        });
}

public sealed class PhotoSampler(SKBitmap bitmap) : IDisposable
{
    public int Width => bitmap.Width;
    public int Height => bitmap.Height;

    /// <summary>Same sampling rule as the server: circle centred at (x,y) 0..1 with radius as a fraction of the shorter side.</summary>
    public List<Rgb> SampleCircle(double x, double y, double radius)
    {
        var w = bitmap.Width; var h = bitmap.Height;
        var cx = x * (w - 1); var cy = y * (h - 1);
        var r = Math.Max(1.0, radius * Math.Min(w, h));
        var x0 = Math.Max(0, (int)Math.Floor(cx - r)); var x1 = Math.Min(w - 1, (int)Math.Ceiling(cx + r));
        var y0 = Math.Max(0, (int)Math.Floor(cy - r)); var y1 = Math.Min(h - 1, (int)Math.Ceiling(cy + r));
        var step = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(Math.PI * r * r / 40_000)));
        var r2 = r * r;
        var pixels = new List<Rgb>();
        for (var py = y0; py <= y1; py += step)
            for (var px = x0; px <= x1; px += step)
            {
                var dx = px - cx; var dy = py - cy;
                if (dx * dx + dy * dy > r2) continue;
                var c = bitmap.GetPixel(px, py);
                pixels.Add(new Rgb(c.Red, c.Green, c.Blue));
            }
        return pixels;
    }

    /// <summary>The region of the photo that best matches the target. Same algorithm as the server, which makes the official choice.</summary>
    public FoundRegion FindBest(Rgb target)
    {
        var scale = Math.Min(1.0, RegionFinder.MaxEdge / (double)Math.Max(bitmap.Width, bitmap.Height));
        var w = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
        var h = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
        using var small = scale < 1.0 ? bitmap.Resize(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)) : bitmap.Copy();
        var px = new Rgb[w * h];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var c = small.GetPixel(x, y);
                px[y * w + x] = new Rgb(c.Red, c.Green, c.Blue);
            }
        return RegionFinder.Find(w, h, px, target);
    }

    /// <summary>Quick local estimate of the best region. The official score is always recomputed by the server.</summary>
    public (ColorPreview? Preview, FoundRegion Region) Preview(Rgb target)
    {
        var region = FindBest(target);
        var pixels = SampleCircle(region.X, region.Y, region.Radius);
        if (pixels.Count < 9) return (null, region);
        var analysis = RegionColorAnalyzer.Analyze(pixels);
        var match = MatchScorer.Compare(target, analysis.Color, region.ColorShare);
        return (new ColorPreview(analysis.Color, match.DeltaE, match.Score, analysis.UsedPixels, region.ColorShare), region);
    }

    public void Dispose() => bitmap.Dispose();
}
