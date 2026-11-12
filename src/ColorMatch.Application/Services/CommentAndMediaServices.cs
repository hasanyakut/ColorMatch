using ColorMatch.Application.Common;
using ColorMatch.Application.DTOs;
using ColorMatch.Application.Interfaces;
using ColorMatch.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ColorMatch.Application.Services;

public sealed class CommentService(IAppDbContext db, TimeProvider clock) : ICommentService
{
    public async Task<CommentDto> AddAsync(Guid userId, Guid postId, CreateCommentRequest request, CancellationToken ct)
    {
        if (!await db.Posts.AnyAsync(p => p.Id == postId, ct)) throw new NotFoundException("Post not found.");
        var text = request.Text.Trim();
        if (text.Length == 0) throw new ValidationAppException("text", "Comment cannot be empty.");

        var comment = new PostComment
        {
            Id = Guid.NewGuid(), PostId = postId, UserId = userId, Text = text,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime
        };
        db.PostComments.Add(comment);
        await db.SaveChangesAsync(ct);

        var author = await db.Profiles.AsNoTracking().FirstAsync(p => p.Id == userId, ct);
        return new CommentDto(comment.Id, new AuthorDto(author.Id, author.UserName, MediaPaths.ToUrl(author.AvatarKey)), comment.Text, comment.CreatedAtUtc);
    }

    public async Task<PagedResult<CommentDto>> ListAsync(Guid postId, string? cursor, int pageSize, CancellationToken ct)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        if (!await db.Posts.AnyAsync(p => p.Id == postId, ct)) throw new NotFoundException("Post not found.");

        IQueryable<PostComment> query = db.PostComments.AsNoTracking().Where(c => c.PostId == postId);
        if (!string.IsNullOrEmpty(cursor))
        {
            if (!Cursor.TryDecode(cursor, out var ts, out var id))
                throw new ValidationAppException("cursor", "Invalid cursor.");
            query = query.Where(c => c.CreatedAtUtc < ts || (c.CreatedAtUtc == ts && c.Id.CompareTo(id) < 0));
        }

        var rows = await query.OrderByDescending(c => c.CreatedAtUtc).ThenByDescending(c => c.Id)
            .Select(c => new { c.Id, c.Text, c.CreatedAtUtc, c.UserId, c.User!.UserName, c.User.AvatarKey })
            .Take(pageSize + 1)
            .ToListAsync(ct);

        var hasMore = rows.Count > pageSize;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        var next = hasMore ? Cursor.Encode(rows[^1].CreatedAtUtc, rows[^1].Id) : null;
        var items = rows.Select(r => new CommentDto(r.Id, new AuthorDto(r.UserId, r.UserName, MediaPaths.ToUrl(r.AvatarKey)), r.Text, r.CreatedAtUtc)).ToList();
        return new PagedResult<CommentDto>(items, next);
    }
}

public sealed class MediaService(IImageProcessor images, IFileStorage storage) : IMediaService
{
    public const int MaxEdge = 2048;

    public async Task<MediaUploadResponse> UploadAsync(Guid userId, Stream content, CancellationToken ct)
    {
        var image = images.Normalize(content, MaxEdge);
        var key = $"{MediaPaths.UserPrefix(userId)}media/{Guid.NewGuid():N}.jpg";
        await using var ms = new MemoryStream(image.JpegBytes);
        await storage.SaveAsync(key, ms, ct);
        return new MediaUploadResponse(key, MediaPaths.ToUrl(key)!);
    }
}
