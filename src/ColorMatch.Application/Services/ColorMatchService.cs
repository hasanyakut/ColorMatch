using ColorMatch.Application.Common;
using ColorMatch.Application.DTOs;
using ColorMatch.Application.Interfaces;
using ColorMatch.ColorEngine.ColorAnalysis;
using ColorMatch.ColorEngine.ColorConversion;
using ColorMatch.ColorEngine.ColorScoring;

namespace ColorMatch.Application.Services;

/// <summary>
/// Result of comparing a photo with a target colour. <see cref="Image"/> is the normalised JPEG that should be stored;
/// <see cref="Region"/> is where the server measured.
/// </summary>
public sealed record ColorMatchOutcome(NormalizedImage Image, Rgb Found, double DeltaE, int Score, FoundRegion Region)
{
    public RegionDto RegionDto => new(Math.Round(Region.X, 4), Math.Round(Region.Y, 4), Math.Round(Region.Radius, 4));
}

/// <summary>
/// The one place where a photo becomes a score: normalise the photo → find the region that best matches the target
/// (<see cref="RegionFinder"/>, decided entirely on the server) → robust representative colour of that region at full resolution →
/// CIEDE2000 against the target → 0–1000. Every game mode uses it, so no mode can drift into its own similarity rule.
/// The client supplies neither a score, a colour nor a region.
/// </summary>
public interface IColorMatchService
{
    ColorMatchOutcome Evaluate(Stream photo, string targetHex);
}

public sealed class ColorMatchService(IImageProcessor images) : IColorMatchService
{
    public const int MinSamplePixels = 9;
    public const int MaxEdge = 2048;

    public ColorMatchOutcome Evaluate(Stream photo, string targetHex)
    {
        if (!Rgb.TryParseHex(targetHex, out var target)) throw new ArgumentException("Invalid target colour.", nameof(targetHex));

        var image = images.Normalize(photo, MaxEdge);
        var grid = images.Downscale(image.JpegBytes, RegionFinder.MaxEdge);
        var region = RegionFinder.Find(grid.Width, grid.Height, grid.Pixels, target);

        var pixels = images.SampleCircle(image.JpegBytes, region.X, region.Y, region.Radius);
        if (pixels.Count < MinSamplePixels)
            throw new ValidationAppException("file", "The photo is too small to analyse. Take a larger photo.");

        var analysis = RegionColorAnalyzer.Analyze(pixels);
        var match = MatchScorer.Compare(target, analysis.Color);
        return new ColorMatchOutcome(image, analysis.Color, match.DeltaE, match.Score, region);
    }
}
