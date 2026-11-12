using ColorMatch.Application.Common;
using ColorMatch.Application.Interfaces;
using ColorMatch.ColorEngine.ColorConversion;
using SkiaSharp;

namespace ColorMatch.Infrastructure.Storage;

/// <summary>
/// Image validation + normalisation (SkiaSharp). Because every upload is fully decoded and re-encoded,
/// non-image payloads are rejected and EXIF metadata (incl. GPS) never reaches storage.
/// </summary>
public sealed class SkiaImageProcessor : IImageProcessor
{
    public const long MaxInputBytes = 12L * 1024 * 1024;
    private const long MaxPixels = 60_000_000; // decompression-bomb guard
    private const int JpegQuality = 88;
    private const int MaxSamples = 40_000;

    public NormalizedImage Normalize(Stream input, int maxEdge)
    {
        var data = ReadBounded(input);
        if (!LooksLikeSupportedImage(data))
            throw new ValidationAppException("file", "Unsupported file type. Use a JPEG, PNG or WebP image.");

        using var codec = SKCodec.Create(new SKMemoryStream(data))
                          ?? throw new ValidationAppException("file", "The file is not a valid image.");
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > MaxPixels)
            throw new ValidationAppException("file", "Image dimensions are not supported.");

        using var bitmap = SKBitmap.Decode(codec)
                           ?? throw new ValidationAppException("file", "The file is not a valid image.");

        var origin = codec.EncodedOrigin;
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        int w = bitmap.Width, h = bitmap.Height;
        int ow = swap ? h : w, oh = swap ? w : h;

        var scale = Math.Min(1.0, maxEdge / (double)Math.Max(ow, oh));
        var tw = Math.Max(1, (int)Math.Round(ow * scale));
        var th = Math.Max(1, (int)Math.Round(oh * scale));
        var s = (float)scale;

        using var target = new SKBitmap(new SKImageInfo(tw, th, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(target))
        {
            canvas.Clear(SKColors.White);
            // matrix maps source pixels into the upright (oriented) space, then uniform scale
            canvas.SetMatrix(OrientationMatrix(origin, w, h, s));
            using var image = SKImage.FromBitmap(bitmap);
            canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), null);
        }

        using var encoded = SKImage.FromBitmap(target).Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
        return new NormalizedImage(encoded.ToArray(), tw, th);
    }

    public IReadOnlyList<Rgb> SampleCircle(byte[] jpegBytes, double centerX, double centerY, double radius)
    {
        using var bitmap = SKBitmap.Decode(jpegBytes) ?? throw new ValidationAppException("file", "The file is not a valid image.");
        var w = bitmap.Width; var h = bitmap.Height;
        var cx = centerX * (w - 1);
        var cy = centerY * (h - 1);
        var r = Math.Max(1.0, radius * Math.Min(w, h));

        var x0 = Math.Max(0, (int)Math.Floor(cx - r));
        var x1 = Math.Min(w - 1, (int)Math.Ceiling(cx + r));
        var y0 = Math.Max(0, (int)Math.Floor(cy - r));
        var y1 = Math.Min(h - 1, (int)Math.Ceiling(cy + r));

        var area = Math.PI * r * r;
        var step = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(area / MaxSamples)));
        var r2 = r * r;

        var pixels = new List<Rgb>();
        for (var y = y0; y <= y1; y += step)
        {
            for (var x = x0; x <= x1; x += step)
            {
                var dx = x - cx; var dy = y - cy;
                if (dx * dx + dy * dy > r2) continue;
                var c = bitmap.GetPixel(x, y);
                pixels.Add(new Rgb(c.Red, c.Green, c.Blue));
            }
        }
        return pixels;
    }

    public PixelGrid Downscale(byte[] jpegBytes, int maxEdge)
    {
        using var bitmap = SKBitmap.Decode(jpegBytes) ?? throw new ValidationAppException("file", "The file is not a valid image.");
        var scale = Math.Min(1.0, maxEdge / (double)Math.Max(bitmap.Width, bitmap.Height));
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
        return new PixelGrid(w, h, px);
    }

    private static SKMatrix OrientationMatrix(SKEncodedOrigin origin, int w, int h, float s)
    {
        // x' = a*x + b*y + tx ; y' = c*x + d*y + ty   (source -> upright)
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

    private static byte[] ReadBounded(Stream input)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            ms.Write(buffer, 0, read);
            if (ms.Length > MaxInputBytes) throw new ValidationAppException("file", "Image is too large (max 12 MB).");
        }
        if (ms.Length == 0) throw new ValidationAppException("file", "File is empty.");
        return ms.ToArray();
    }

    private static bool LooksLikeSupportedImage(byte[] d)
    {
        if (d.Length < 12) return false;
        var jpeg = d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF;
        var png = d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47;
        var webp = d[0] == 'R' && d[1] == 'I' && d[2] == 'F' && d[3] == 'F' && d[8] == 'W' && d[9] == 'E' && d[10] == 'B' && d[11] == 'P';
        return jpeg || png || webp;
    }
}
