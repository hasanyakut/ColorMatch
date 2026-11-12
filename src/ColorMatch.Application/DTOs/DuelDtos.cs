namespace ColorMatch.Application.DTOs;

public sealed record CreateDuelRequest(Guid OpponentId);

/// <summary>
/// One duel from the caller's point of view. Fairness rules baked into the shape: the opponent's colour is hidden until they accept,
/// and the other player's best score (<see cref="OtherBest"/>) is null until the duel is over (both done, or expired).
/// </summary>
public sealed record DuelDto(
    Guid Id,
    string Status,                       // Pending | Active | Completed | Expired | Declined
    string Role,                         // challenger | opponent
    Guid OtherUserId,
    string OtherUserName,
    string? OtherAvatarUrl,
    string? TargetHex,                   // null for the invited player until they accept
    string? TargetName,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc,
    int AttemptsPerPlayer,
    IReadOnlyList<int> MyScores,
    int MyAttemptsLeft,
    int? MyBest,
    bool MyDone,
    bool OtherDone,
    int OtherAttempts,
    int? OtherBest,                      // hidden until the duel is over
    string? Outcome,                     // win | lose | draw | none (nobody played), only once over
    int MyXp,
    bool CanPlay,
    bool NeedsMyResponse,
    int WinXp,
    int DrawXp,
    int LoseXp);

public sealed record DuelListDto(IReadOnlyList<DuelDto> Duels, int OpenCount, int MaxOpen, int WinXp, int DrawXp, int LoseXp);

public sealed record DuelAttemptResultDto(
    Guid AttemptId,
    string TargetHex,
    string FoundHex,
    double DeltaE,
    int Score,
    string Tier,
    bool IsNewBest,
    int MyBest,
    int AttemptsLeft,
    bool Finished,                       // the caller has no attempts left / chose to stop
    bool DuelResolved,                   // this attempt ended the duel
    int XpAwarded,
    int TotalXp,
    int Level,
    bool LeveledUp,
    DuelDto Duel,
    RegionDto? Region = null);
