namespace ColorMatch.Application.DTOs;

// ---- Friends / discovery ----
public sealed record FriendDto(Guid UserId, string UserName, string? AvatarUrl, int Level, DateTime? SinceUtc);

public sealed record FriendRequestsDto(IReadOnlyList<FriendDto> Incoming, IReadOnlyList<FriendDto> Outgoing);

/// <summary>Relationship values: none | friends | request_sent | request_received | self</summary>
public sealed record UserSearchResultDto(Guid UserId, string UserName, string? AvatarUrl, int Level, string Relationship);

public sealed record RelationshipDto(string Relationship);

public sealed record SendFriendRequestBody(Guid UserId);

public sealed record ColorFacetDto(string Family, string Hex, int Count);
