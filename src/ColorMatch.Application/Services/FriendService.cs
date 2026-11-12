using ColorMatch.Application.Common;
using ColorMatch.Application.DTOs;
using ColorMatch.Application.Interfaces;
using ColorMatch.Domain.Entities;
using ColorMatch.Domain.Enums;
using ColorMatch.Domain.Rules;
using Microsoft.EntityFrameworkCore;

namespace ColorMatch.Application.Services;

public sealed class FriendService(IAppDbContext db, TimeProvider clock, LevelCurve curve) : IFriendService
{
    private const string None = "none", Friends = "friends", Sent = "request_sent", Received = "request_received", Self = "self";

    public async Task<IReadOnlyList<UserSearchResultDto>> SearchAsync(Guid userId, string query, CancellationToken ct)
    {
        query = (query ?? "").Trim();
        if (query.Length < 2) throw new ValidationAppException("q", "Arama için en az 2 karakter girin.");

        // LIKE wildcards typed by the user are escaped so they match literally
        var escaped = query.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");

        var users = await db.Profiles.AsNoTracking()
            .Where(p => p.Id != userId && EF.Functions.Like(p.UserName, escaped + "%", "\\"))
            .OrderBy(p => p.UserName)
            .Take(20)
            .Select(p => new { p.Id, p.UserName, p.AvatarKey, Xp = p.Progress != null ? p.Progress.TotalXp : 0 })
            .ToListAsync(ct);

        var ids = users.Select(u => u.Id).ToList();
        var rels = await db.Friendships.AsNoTracking()
            .Where(f => (f.RequesterId == userId && ids.Contains(f.AddresseeId)) || (f.AddresseeId == userId && ids.Contains(f.RequesterId)))
            .ToListAsync(ct);

        return users.Select(u =>
        {
            var f = rels.FirstOrDefault(r => r.RequesterId == u.Id || r.AddresseeId == u.Id);
            return new UserSearchResultDto(u.Id, u.UserName, MediaPaths.ToUrl(u.AvatarKey), curve.LevelForXp(u.Xp), Describe(f, userId));
        }).ToList();
    }

    public async Task<IReadOnlyList<FriendDto>> ListFriendsAsync(Guid userId, CancellationToken ct)
    {
        var rows = await db.Friendships.AsNoTracking()
            .Where(f => f.Status == FriendshipStatus.Accepted && (f.RequesterId == userId || f.AddresseeId == userId))
            .Select(f => new
            {
                Id = f.RequesterId == userId ? f.AddresseeId : f.RequesterId,
                f.RespondedAtUtc
            })
            .Join(db.Profiles.AsNoTracking(), x => x.Id, p => p.Id,
                (x, p) => new { p.Id, p.UserName, p.AvatarKey, Xp = p.Progress != null ? p.Progress.TotalXp : 0, x.RespondedAtUtc })
            .OrderBy(x => x.UserName)
            .ToListAsync(ct);
        return rows.Select(r => new FriendDto(r.Id, r.UserName, MediaPaths.ToUrl(r.AvatarKey), curve.LevelForXp(r.Xp), r.RespondedAtUtc)).ToList();
    }

    public async Task<FriendRequestsDto> ListRequestsAsync(Guid userId, CancellationToken ct)
    {
        var incoming = await db.Friendships.AsNoTracking()
            .Where(f => f.Status == FriendshipStatus.Pending && f.AddresseeId == userId)
            .OrderByDescending(f => f.CreatedAtUtc)
            .Select(f => new { f.Requester!.Id, f.Requester.UserName, f.Requester.AvatarKey, Xp = f.Requester.Progress != null ? f.Requester.Progress.TotalXp : 0, f.CreatedAtUtc })
            .ToListAsync(ct);
        var outgoing = await db.Friendships.AsNoTracking()
            .Where(f => f.Status == FriendshipStatus.Pending && f.RequesterId == userId)
            .OrderByDescending(f => f.CreatedAtUtc)
            .Select(f => new { f.Addressee!.Id, f.Addressee.UserName, f.Addressee.AvatarKey, Xp = f.Addressee.Progress != null ? f.Addressee.Progress.TotalXp : 0, f.CreatedAtUtc })
            .ToListAsync(ct);

        FriendDto Map(Guid id, string name, string? avatar, int xp, DateTime at)
            => new(id, name, MediaPaths.ToUrl(avatar), curve.LevelForXp(xp), at);

        return new FriendRequestsDto(
            incoming.Select(x => Map(x.Id, x.UserName, x.AvatarKey, x.Xp, x.CreatedAtUtc)).ToList(),
            outgoing.Select(x => Map(x.Id, x.UserName, x.AvatarKey, x.Xp, x.CreatedAtUtc)).ToList());
    }

    public async Task<RelationshipDto> SendRequestAsync(Guid userId, Guid targetId, CancellationToken ct)
    {
        if (userId == targetId) throw new ValidationAppException("userId", "Kendinize arkadaşlık isteği gönderemezsiniz.");
        if (!await db.Profiles.AnyAsync(p => p.Id == targetId, ct)) throw new NotFoundException("Kullanıcı bulunamadı.");

        var existing = await Find(userId, targetId, ct);
        if (existing is not null)
        {
            if (existing.Status == FriendshipStatus.Pending && existing.AddresseeId == userId)
                return await AcceptAsync(userId, targetId, ct); // they already asked us: mutual intent
            return new RelationshipDto(Describe(existing, userId)); // already friends / already requested: idempotent
        }

        db.Friendships.Add(new Friendship
        {
            Id = Guid.NewGuid(),
            RequesterId = userId,
            AddresseeId = targetId,
            Status = FriendshipStatus.Pending,
            PairKey = Friendship.MakePairKey(userId, targetId),
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime
        });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // the pair row appeared in parallel (unique PairKey): report the resulting state
            return await GetRelationshipAsync(userId, targetId, ct);
        }
        return new RelationshipDto(Sent);
    }

    public async Task<RelationshipDto> AcceptAsync(Guid userId, Guid requesterId, CancellationToken ct)
    {
        var f = await Find(userId, requesterId, ct) ?? throw new NotFoundException("Arkadaşlık isteği bulunamadı.");
        if (f.Status == FriendshipStatus.Accepted) return new RelationshipDto(Friends);
        if (f.AddresseeId != userId) throw new ForbiddenException("Yalnızca size gelen istekleri kabul edebilirsiniz.");

        f.Status = FriendshipStatus.Accepted;
        f.RespondedAtUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return new RelationshipDto(Friends);
    }

    public async Task<RelationshipDto> RemoveAsync(Guid userId, Guid otherId, CancellationToken ct)
    {
        var f = await Find(userId, otherId, ct);
        if (f is not null)
        {
            db.Friendships.Remove(f);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { /* already removed */ }
        }
        return new RelationshipDto(otherId == userId ? Self : None);
    }

    public async Task<RelationshipDto> GetRelationshipAsync(Guid userId, Guid otherId, CancellationToken ct)
    {
        if (userId == otherId) return new RelationshipDto(Self);
        return new RelationshipDto(Describe(await Find(userId, otherId, ct), userId));
    }

    private Task<Friendship?> Find(Guid a, Guid b, CancellationToken ct)
    {
        var key = Friendship.MakePairKey(a, b);
        return db.Friendships.FirstOrDefaultAsync(f => f.PairKey == key, ct);
    }

    private static string Describe(Friendship? f, Guid me)
    {
        if (f is null) return None;
        if (f.Status == FriendshipStatus.Accepted) return Friends;
        return f.RequesterId == me ? Sent : Received;
    }
}
