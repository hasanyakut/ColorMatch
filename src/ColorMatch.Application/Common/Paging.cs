using System.Globalization;

namespace ColorMatch.Application.Common;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, string? NextCursor);

/// <summary>Keyset cursor "ticks:guid" over (CreatedAtUtc, Id).</summary>
public static class Cursor
{
    public static string Encode(DateTime createdAtUtc, Guid id) =>
        $"{createdAtUtc.Ticks.ToString(CultureInfo.InvariantCulture)}:{id:N}";

    public static bool TryDecode(string? cursor, out DateTime createdAtUtc, out Guid id)
    {
        createdAtUtc = default; id = default;
        if (string.IsNullOrEmpty(cursor)) return false;
        var parts = cursor.Split(':');
        if (parts.Length != 2) return false;
        if (!long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)) return false;
        if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks) return false;
        if (!Guid.TryParseExact(parts[1], "N", out id)) return false;
        createdAtUtc = new DateTime(ticks, DateTimeKind.Utc);
        return true;
    }
}

public static class MediaPaths
{
    public const string UrlPrefix = "/media/";
    public static string? ToUrl(string? key) => string.IsNullOrEmpty(key) ? null : UrlPrefix + key;
    public static string UserPrefix(Guid userId) => $"u/{userId:N}/";
}
