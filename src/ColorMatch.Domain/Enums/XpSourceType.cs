namespace ColorMatch.Domain.Enums;

/// <summary>Which activity produced an XP transaction. Stored as text so the ledger stays readable.</summary>
public enum XpSourceType
{
    /// <summary>XP that existed before the ledger was introduced (one entry per player).</summary>
    LegacyImport,
    /// <summary>A colour hunt attempt (current single-colour hunt).</summary>
    HuntAttempt,
    DailyHunt,
    DailyHuntCompletion,
    AtlasColor,
    AtlasCollectionComplete,
    BingoLine,
    BingoBoardComplete,
    DuelResult,
    Achievement,
}
