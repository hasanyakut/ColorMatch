using ColorMatch.Application.Common;
using ColorMatch.Application.DTOs;
using ColorMatch.Application.Interfaces;
using ColorMatch.ColorEngine.ColorAnalysis;
using ColorMatch.Domain.Entities;
using ColorMatch.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ColorMatch.Application.Services;

public sealed class PostService(IAppDbContext db, IFileStorage storage, TimeProvider clock) : IPostService
{
    public const int MaxPageSize = 50;

    public async Task<PostDto> CreateAsync(Guid userId, CreatePostRequest request, CancellationToken ct)
    {
        var keys = request.MediaKeys.Select(k => k.Trim()).Distinct(StringComparer.Ordinal).ToList();
        var prefix = MediaPaths.UserPrefix(userId);
        foreach (var key in keys)
        {
            // keys must be ones this user uploaded (no path tricks, no other user's media)
            if (!key.StartsWith(prefix, StringComparison.Ordinal) || key.Contains("..") || key.Contains('\\'))
                throw new ValidationAppException("mediaKeys", "Invalid media key.");
            if (!await storage.ExistsAsync(key, ct))
                throw new ValidationAppException("mediaKeys", "Media was not found. Upload it first.");
        }

        var post = new Post
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Type = request.Type,
            Caption = string.IsNullOrWhiteSpace(request.Caption) ? null : request.Caption.Trim(),
            ColorHex = request.ColorHex?.ToUpperInvariant(),
            ColorFamily = ColorFamilies.Classify(request.ColorHex),
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
            Media = keys.Select((k, i) => new PostMedia { Id = Guid.NewGuid(), StorageKey = k, SortOrder = i }).ToList()
        };
        db.Posts.Add(post);
        await db.SaveChangesAsync(ct);
        return await GetAsync(userId, post.Id, ct);
    }

    public async Task<PostDto> GetAsync(Guid? viewerId, Guid postId, CancellationToken ct)
    {
        var row = await Project(db.Posts.AsNoTracking().Where(p => p.Id == postId), viewerId).FirstOrDefaultAsync(ct)
                  ?? throw new NotFoundException("Post not found.");
        return Map(row);
    }

    public async Task<IReadOnlyList<ColorFacetDto>> GetColorFacetsAsync(CancellationToken ct)
    {
        var counts = await db.Posts.AsNoTracking()
            .Where(p => p.ColorFamily != null)
            .GroupBy(p => p.ColorFamily!)
            .Select(g => new { Family = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // stable, palette-ordered list; families nobody posted in yet are simply omitted
        return ColorFamilies.All
            .Join(counts, f => f.Name, c => c.Family, (f, c) => new ColorFacetDto(f.Name, f.Hex, c.Count))
            .ToList();
    }

    public async Task<PagedResult<PostDto>> GetFeedAsync(Guid? viewerId, Guid? authorId, FeedScope scope, string? colorFamily, string? cursor, int pageSize, CancellationToken ct)
    {
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        IQueryable<Post> query = db.Posts.AsNoTracking();
        if (authorId is { } a) query = query.Where(p => p.UserId == a);

        if (scope == FeedScope.Friends)
        {
            if (viewerId is not { } me) throw new ValidationAppException("scope", "Arkadaş akışı için giriş gerekli.");
            query = query.Where(p => p.UserId == me
                || db.Friendships.Any(f => f.Status == FriendshipStatus.Accepted
                    && ((f.RequesterId == me && f.AddresseeId == p.UserId) || (f.AddresseeId == me && f.RequesterId == p.UserId))));
        }

        if (!string.IsNullOrWhiteSpace(colorFamily))
        {
            if (!ColorFamilies.IsKnown(colorFamily)) throw new ValidationAppException("color", "Bilinmeyen renk ailesi.");
            query = query.Where(p => p.ColorFamily == colorFamily);
        }

        if (!string.IsNullOrEmpty(cursor))
        {
            if (!Cursor.TryDecode(cursor, out var ts, out var id))
                throw new ValidationAppException("cursor", "Invalid cursor.");
            query = query.Where(p => p.CreatedAtUtc < ts || (p.CreatedAtUtc == ts && p.Id.CompareTo(id) < 0));
        }

        var rows = await Project(query.OrderByDescending(p => p.CreatedAtUtc).ThenByDescending(p => p.Id), viewerId)
            .Take(pageSize + 1)
            .ToListAsync(ct);

        var hasMore = rows.Count > pageSize;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        var next = hasMore ? Cursor.Encode(rows[^1].CreatedAtUtc, rows[^1].Id) : null;
        return new PagedResult<PostDto>(rows.Select(Map).ToList(), next);
    }

    public async Task<LikeStateDto> SetLikeAsync(Guid userId, Guid postId, bool liked, CancellationToken ct)
    {
        if (!await db.Posts.AnyAsync(p => p.Id == postId, ct)) throw new NotFoundException("Post not found.");

        var existing = await db.PostLikes.FirstOrDefaultAsync(l => l.PostId == postId && l.UserId == userId, ct);
        if (liked && existing is null)
        {
            db.PostLikes.Add(new PostLike { PostId = postId, UserId = userId, CreatedAtUtc = clock.GetUtcNow().UtcDateTime });
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { /* duplicate like from a parallel request: already liked */ }
        }
        else if (!liked && existing is not null)
        {
            db.PostLikes.Remove(existing);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { /* already removed */ }
        }

        var count = await db.PostLikes.CountAsync(l => l.PostId == postId, ct);
        var isLiked = await db.PostLikes.AnyAsync(l => l.PostId == postId && l.UserId == userId, ct);
        return new LikeStateDto(isLiked, count);
    }

    public async Task DeleteAsync(Guid userId, Guid postId, CancellationToken ct)
    {
        var post = await db.Posts.Include(p => p.Media).FirstOrDefaultAsync(p => p.Id == postId, ct)
                   ?? throw new NotFoundException("Post not found.");
        if (post.UserId != userId) throw new ForbiddenException("You can only delete your own posts.");

        var keys = post.Media.Select(m => m.StorageKey).ToList();
        db.Posts.Remove(post);
        await db.SaveChangesAsync(ct);

        foreach (var key in keys)
        {
            // keep files still referenced by other rows (e.g. a hunt photo reused elsewhere)
            if (!await db.PostMedia.AnyAsync(m => m.StorageKey == key, ct))
                await storage.DeleteAsync(key, ct);
        }
    }

    private sealed record PostRow(Guid Id, DateTime CreatedAtUtc, Guid AuthorId, string AuthorName, string? AuthorAvatar,
        Domain.Enums.PostType Type, string? Caption, string? ColorHex, List<string> Media, int Likes, int Comments, bool Liked);

    private IQueryable<PostRow> Project(IQueryable<Post> query, Guid? viewerId) =>
        query.Select(p => new PostRow(
            p.Id, p.CreatedAtUtc, p.UserId, p.User!.UserName, p.User.AvatarKey, p.Type, p.Caption, p.ColorHex,
            p.Media.OrderBy(m => m.SortOrder).Select(m => m.StorageKey).ToList(),
            p.Likes.Count, p.Comments.Count,
            viewerId != null && p.Likes.Any(l => l.UserId == viewerId)));

    private static PostDto Map(PostRow r) => new(
        r.Id, new AuthorDto(r.AuthorId, r.AuthorName, MediaPaths.ToUrl(r.AuthorAvatar)), r.Type, r.Caption, r.ColorHex,
        r.Media.Select(k => MediaPaths.ToUrl(k)!).ToList(), r.Likes, r.Comments, r.Liked, r.CreatedAtUtc);
}
