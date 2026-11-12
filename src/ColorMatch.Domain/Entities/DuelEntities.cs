namespace ColorMatch.Domain.Entities;

/// <summary>Pending = invitation sent, Active = accepted and the 24 h clock runs, Completed = both players finished, Expired = time ran out, Declined = refused or withdrawn.</summary>
public enum DuelStatus { Pending, Active, Completed, Expired, Declined }

/// <summary>
/// An asynchronous friend duel: both players photograph the same target colour (3 attempts each) within 24 hours of acceptance; the better
/// best score wins. Expiry is applied lazily whenever a duel is read or played, there is no background timer.
/// </summary>
public class Duel
{
    public Guid Id { get; set; }
    public Guid ChallengerId { get; set; }
    public Guid OpponentId { get; set; }
    /// <summary>Key of the target colour in <c>AtlasCatalog</c>; the opponent only sees it after accepting.</summary>
    public string ColorKey { get; set; } = string.Empty;
    public DuelStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
    /// <summary>Invitation deadline while Pending, play deadline while Active.</summary>
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    /// <summary>Touched on every attempt, so the row version rejects two parallel attempts that would exceed the attempt limit.</summary>
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>A player is done after the last attempt or by choosing to stop.</summary>
    public bool ChallengerDone { get; set; }
    public bool OpponentDone { get; set; }

    /// <summary>Null on a draw (and while unresolved).</summary>
    public Guid? WinnerId { get; set; }
    public int? ChallengerBest { get; set; }
    public int? OpponentBest { get; set; }
    public int ChallengerXp { get; set; }
    public int OpponentXp { get; set; }

    /// <summary>Concurrency token: a duel is resolved (and its XP paid) exactly once.</summary>
    public byte[] RowVersion { get; set; } = [];

    public UserProfile? Challenger { get; set; }
    public UserProfile? Opponent { get; set; }
}

/// <summary>One try in a duel. The photo is not stored, only its hash (basic copy check).</summary>
public class DuelAttempt
{
    public Guid Id { get; set; }
    public Guid DuelId { get; set; }
    public Guid UserId { get; set; }
    public int Score { get; set; }
    public double DeltaE { get; set; }
    public string SampledColorHex { get; set; } = string.Empty;
    public string PhotoHash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
