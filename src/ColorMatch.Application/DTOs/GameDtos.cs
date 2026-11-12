namespace ColorMatch.Application.DTOs;

/// <summary>The player's progression: XP, level, title and streak. Read-only for clients.</summary>
public sealed record ProgressionDto(
    int TotalXp,
    int Level,
    string Title,
    string? NextTitle,
    int? NextTitleLevel,
    int LevelStartXp,
    int NextLevelXp,
    int XpIntoLevel,
    int XpToNextLevel,
    double Progress,
    int CurrentStreak,
    int LongestStreak,
    DateOnly? LastQualifyingActivityDate,
    bool StreakAtRisk);

/// <summary>One XP ledger entry.</summary>
public sealed record XpTransactionDto(
    Guid Id,
    int Amount,
    string SourceType,
    string? SourceId,
    string Reason,
    int BalanceAfter,
    DateTime CreatedAtUtc);

// ---- Daily Hunt ----
/// <summary>One of today's three hunts, with the caller's own state.</summary>
public sealed record DailyHuntDto(
    int Id,
    DateOnly Date,
    string Difficulty,
    string Name,
    string Hex,
    int BaseXp,
    int MinScore,
    int BestScore,
    string? BestTier,
    bool Completed,
    int Attempts,
    int XpEarned,
    string? BestPhotoUrl);

/// <summary>One game day of the player's last week: how many of that day's hunts were completed.</summary>
public sealed record WeekDayDto(DateOnly Date, int Completed, int Total, bool IsToday);

/// <summary>The last seven game days (oldest first, today last) for the week strip on the home screen.</summary>
public sealed record WeekDto(IReadOnlyList<WeekDayDto> Days, int ActiveDays, int PerfectDays);

public sealed record DailyProgressDto(
    int Completed,
    int Total,
    bool AllCompleted,
    int CompletionBonusXp,
    bool BonusEarned,
    int XpEarnedToday,
    int XpAvailableToday);

public sealed record DailyHuntsDto(
    DateOnly Date,
    DateTime ResetsAtUtc,
    IReadOnlyList<DailyHuntDto> Hunts,
    DailyProgressDto Progress);

/// <summary>Outcome of one attempt. Everything here is computed on the server.</summary>
public sealed record DailyHuntAttemptResultDto(
    Guid AttemptId,
    int HuntId,
    string Difficulty,
    string TargetHex,
    string FoundHex,
    double DeltaE,
    int Score,
    string Tier,
    bool Passed,
    int MinScore,
    int XpAwarded,
    bool FirstCompletion,
    int BestScore,
    bool IsNewBest,
    int BonusXpAwarded,
    int CompletedCount,
    bool AllCompleted,
    int TotalXp,
    int Level,
    bool LeveledUp,
    string Title,
    bool TitleChanged,
    int CurrentStreak,
    bool StreakExtended,
    string PhotoKey,
    string PhotoUrl,
    AtlasUnlockDto? AtlasUnlock = null,
    RegionDto? Region = null);

// ---- Color Atlas ----
/// <summary>One atlas colour as the caller sees it. Locked colours reveal neither name nor hex (unless one of today's hunts asks for them).</summary>
public sealed record AtlasColorDto(
    string Key,
    string? Name,
    string? Hex,
    bool Unlocked,
    DateTime? UnlockedAtUtc,
    int? BestScore,
    bool IsTodaysHunt,
    string Difficulty);

public sealed record AtlasCollectionDto(
    string Key,
    string Name,
    string Description,
    int Unlocked,
    int Total,
    bool Completed,
    bool CompletionXpEarned,
    int CompletionXp,
    IReadOnlyList<AtlasColorDto> Colors);

public sealed record AtlasDto(
    int Unlocked,
    int Total,
    IReadOnlyList<AtlasCollectionDto> Collections);

/// <summary>What a valid hunt attempt added to the atlas (only present when it unlocked a new colour).</summary>
public sealed record AtlasUnlockDto(
    string ColorKey,
    string Name,
    string Hex,
    string CollectionKey,
    string CollectionName,
    int CollectionUnlocked,
    int CollectionTotal,
    bool CollectionCompleted,
    int CompletionXpAwarded);

// ---- Color Bingo ----
public sealed record BingoCellDto(
    int Index,
    string ColorKey,
    string Name,
    string Hex,
    string Difficulty,
    bool Completed,
    int BestScore,
    int Attempts,
    int AttemptsLeft);

public sealed record BingoLineDto(
    string Id,
    string Name,
    IReadOnlyList<int> Cells,
    bool Completed,
    int Xp);

public sealed record BingoBoardDto(
    Guid BoardId,
    string Status,
    DateTime CreatedAtUtc,
    IReadOnlyList<BingoCellDto> Cells,
    IReadOnlyList<BingoLineDto> Lines,
    int CompletedCells,
    int CompletedLines,
    bool BoardCompleted,
    int LineXp,
    int BoardXp,
    int MinScore,
    int MaxAttemptsPerCell,
    int BoardsCompleted);

public sealed record BingoLineRewardDto(string Id, string Name, int Xp);

/// <summary>Outcome of one attempt on a square. Everything here is computed on the server.</summary>
public sealed record BingoAttemptResultDto(
    Guid AttemptId,
    int CellIndex,
    string ColorName,
    string TargetHex,
    string FoundHex,
    double DeltaE,
    int Score,
    string Tier,
    bool Passed,
    int MinScore,
    bool CellCompleted,
    int BestScore,
    bool IsNewBest,
    int CompletedCells,
    int CompletedLines,
    IReadOnlyList<BingoLineRewardDto> NewLines,
    int LineXpAwarded,
    bool BoardCompleted,
    int BoardXpAwarded,
    int TotalXp,
    int Level,
    bool LeveledUp,
    string Title,
    bool TitleChanged,
    AtlasUnlockDto? AtlasUnlock = null,
    RegionDto? Region = null);
