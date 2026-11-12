namespace ColorMatch.Mobile.Models;

// ---- Friend duels (client mirrors of the API DTOs) ----
public sealed record Duel(
    Guid Id, string Status, string Role, Guid OtherUserId, string OtherUserName, string? OtherAvatarUrl, string? TargetHex, string? TargetName,
    DateTime CreatedAtUtc, DateTime ExpiresAtUtc, int AttemptsPerPlayer, List<int> MyScores, int MyAttemptsLeft, int? MyBest, bool MyDone,
    bool OtherDone, int OtherAttempts, int? OtherBest, string? Outcome, int MyXp, bool CanPlay, bool NeedsMyResponse, int WinXp, int DrawXp, int LoseXp);

public sealed record DuelList(List<Duel> Duels, int OpenCount, int MaxOpen, int WinXp, int DrawXp, int LoseXp);

public sealed record DuelAttemptResult(
    Guid AttemptId, string TargetHex, string FoundHex, double DeltaE, int Score, string Tier, bool IsNewBest, int MyBest, int AttemptsLeft,
    bool Finished, bool DuelResolved, int XpAwarded, int TotalXp, int Level, bool LeveledUp, Duel Duel, RegionInfo? Region = null);

public sealed record CreateDuelBody(Guid OpponentId);
