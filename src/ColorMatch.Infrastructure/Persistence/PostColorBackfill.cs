using ColorMatch.ColorEngine.ColorAnalysis;
using Microsoft.EntityFrameworkCore;

namespace ColorMatch.Infrastructure.Persistence;

/// <summary>Fills <c>Post.ColorFamily</c> for posts created before the column existed.</summary>
public static class PostColorBackfill
{
    public static async Task RunAsync(AppDbContext db, CancellationToken ct = default)
    {
        var pending = await db.Posts
            .Where(p => p.ColorHex != null && p.ColorFamily == null)
            .Take(1000)
            .ToListAsync(ct);
        if (pending.Count == 0) return;

        foreach (var post in pending)
            post.ColorFamily = ColorFamilies.Classify(post.ColorHex);
        await db.SaveChangesAsync(ct);
    }
}
