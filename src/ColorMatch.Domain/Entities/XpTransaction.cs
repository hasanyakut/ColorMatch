using ColorMatch.Domain.Enums;

namespace ColorMatch.Domain.Entities;

/// <summary>
/// One row of the XP ledger. <see cref="UserProgress.TotalXp"/> is always the sum of a player's transactions; both change in the
/// same database transaction. <see cref="IdempotencyKey"/> is unique, so the same reward can never be booked twice.
/// </summary>
public class XpTransaction
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public int Amount { get; set; }
    public XpSourceType SourceType { get; set; }
    /// <summary>Id of the thing that earned the XP (attempt, board, duel …) – informational.</summary>
    public string? SourceId { get; set; }
    public string Reason { get; set; } = string.Empty;
    /// <summary>Globally unique key, e.g. "hunt-attempt:{attemptId}". Built by the service, never by the client.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
    public int BalanceAfter { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
