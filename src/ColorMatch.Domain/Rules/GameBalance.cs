namespace ColorMatch.Domain.Rules;

/// <summary>
/// Every tunable number of the game lives here (bound from the "GameBalance" configuration section), so balancing
/// never needs a code change. Defaults are the first-release values. Later phases add their own groups
/// (Daily Hunt, Atlas, Bingo, Duels) to this class.
/// </summary>
public sealed class GameBalance
{
    public const string Section = "GameBalance";

    /// <summary>XP needed for level n is <c>LevelBaseXp · (n−1)^LevelExponent</c>. 100 / 2.0 reproduces the original curve: 0, 100, 400, 900 …</summary>
    public int LevelBaseXp { get; set; } = 100;
    public double LevelExponent { get; set; } = 2.0;

    public DailyHuntBalance DailyHunt { get; set; } = new();
    public AtlasBalance Atlas { get; set; } = new();
    public BingoBalance Bingo { get; set; } = new();
    public DuelBalance Duel { get; set; } = new();

    /// <summary>Player titles by minimum level. Leave empty to use <see cref="DefaultTitles"/> (configuration lists append to defaults, so the defaults are applied only when this is empty).</summary>
    public List<TitleTier> Titles { get; set; } = [];

    public static readonly IReadOnlyList<TitleTier> DefaultTitles =
    [
        new(1, "Color Beginner"),
        new(5, "Color Seeker"),
        new(10, "Color Explorer"),
        new(20, "Color Hunter"),
        new(35, "Color Master"),
    ];

    public IReadOnlyList<TitleTier> EffectiveTitles => Titles.Count > 0 ? Titles.OrderBy(t => t.MinLevel).ToList() : DefaultTitles;
}

public sealed record TitleTier(int MinLevel, string Name)
{
    public TitleTier() : this(1, "") { }
}

/// <summary>Daily Hunt numbers. XP is paid once per hunt, on the first valid completion.</summary>
public sealed class DailyHuntBalance
{
    public int EasyXp { get; set; } = 100;
    public int MediumXp { get; set; } = 200;
    public int HardXp { get; set; } = 350;

    /// <summary>Minimum score that counts as a completed hunt (and keeps the streak alive). 700 ≈ ΔE00 5.3.</summary>
    public int MinScore { get; set; } = 700;

    /// <summary>Extra XP once all three hunts of a day are completed.</summary>
    public int CompletionBonusXp { get; set; } = 150;

    /// <summary>Upper bound of attempts per hunt and day: keeps uploads (storage) and brute-force guessing in check.</summary>
    public int MaxAttemptsPerHunt { get; set; } = 30;

    public int XpFor(ColorMatch.Domain.Enums.HuntDifficulty d) => d switch
    {
        ColorMatch.Domain.Enums.HuntDifficulty.Easy => EasyXp,
        ColorMatch.Domain.Enums.HuntDifficulty.Medium => MediumXp,
        _ => HardXp,
    };
}

/// <summary>Color Atlas numbers. Unlocking a colour pays nothing by itself; finishing a whole collection pays once.</summary>
public sealed class AtlasBalance
{
    /// <summary>XP paid once per collection (and player) when all of its colours are unlocked.</summary>
    public int CollectionCompleteXp { get; set; } = 500;
}

/// <summary>Friend duel numbers. A player who took part is paid on resolution; a player who never played gets nothing.</summary>
public sealed class DuelBalance
{
    public int WinXp { get; set; } = 200;
    public int DrawXp { get; set; } = 100;
    /// <summary>Consolation for losing after at least one attempt.</summary>
    public int LoseXp { get; set; } = 50;
    public int AttemptsPerPlayer { get; set; } = 3;
    /// <summary>Hours to answer an invitation, and hours to play once it is accepted.</summary>
    public int InviteHours { get; set; } = 24;
    public int PlayHours { get; set; } = 24;
    /// <summary>Open (pending or active) duels one player may be part of: keeps invitations from being spammed.</summary>
    public int MaxOpenDuels { get; set; } = 5;
    /// <summary>Most duel XP one player can earn per game day (UTC day of the payout): limits XP farming between two accounts. 0 = no limit.</summary>
    public int DailyXpCap { get; set; } = 600;
}

/// <summary>Color Bingo numbers. A line pays once per board; finishing all nine squares pays the board bonus once.</summary>
public sealed class BingoBalance
{
    public int LineXp { get; set; } = 100;
    public int BoardCompleteXp { get; set; } = 400;
    /// <summary>Minimum score for a square to count (same bar as a daily hunt).</summary>
    public int MinScore { get; set; } = 700;
    /// <summary>Upper bound of attempts per square: keeps uploads and brute-force guessing in check.</summary>
    public int MaxAttemptsPerCell { get; set; } = 10;
}
