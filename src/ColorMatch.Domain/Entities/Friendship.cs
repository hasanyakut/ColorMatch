using ColorMatch.Domain.Enums;

namespace ColorMatch.Domain.Entities;

/// <summary>A friend request (Pending) that becomes a mutual friendship once accepted.</summary>
public class Friendship
{
    public Guid Id { get; set; }
    public Guid RequesterId { get; set; }
    public Guid AddresseeId { get; set; }
    public FriendshipStatus Status { get; set; }
    /// <summary>Order-independent "smallerId:largerId" key; its unique index guarantees one row per pair even under races.</summary>
    public string PairKey { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? RespondedAtUtc { get; set; }

    public UserProfile? Requester { get; set; }
    public UserProfile? Addressee { get; set; }

    public static string MakePairKey(Guid a, Guid b)
        => a.CompareTo(b) <= 0 ? $"{a:N}:{b:N}" : $"{b:N}:{a:N}";
}
