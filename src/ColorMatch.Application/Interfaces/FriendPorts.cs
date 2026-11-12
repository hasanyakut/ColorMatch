using ColorMatch.Application.DTOs;

namespace ColorMatch.Application.Interfaces;

public enum FeedScope
{
    /// <summary>Everyone's posts (discovery).</summary>
    All = 0,
    /// <summary>The caller's own posts plus accepted friends' posts.</summary>
    Friends = 1
}

public interface IFriendService
{
    Task<IReadOnlyList<UserSearchResultDto>> SearchAsync(Guid userId, string query, CancellationToken ct);
    Task<IReadOnlyList<FriendDto>> ListFriendsAsync(Guid userId, CancellationToken ct);
    Task<FriendRequestsDto> ListRequestsAsync(Guid userId, CancellationToken ct);
    /// <summary>Sends a request; if the other person already asked us, the request is accepted instead.</summary>
    Task<RelationshipDto> SendRequestAsync(Guid userId, Guid targetId, CancellationToken ct);
    Task<RelationshipDto> AcceptAsync(Guid userId, Guid requesterId, CancellationToken ct);
    /// <summary>Declines an incoming request, cancels an outgoing one or removes a friend.</summary>
    Task<RelationshipDto> RemoveAsync(Guid userId, Guid otherId, CancellationToken ct);
    Task<RelationshipDto> GetRelationshipAsync(Guid userId, Guid otherId, CancellationToken ct);
}
