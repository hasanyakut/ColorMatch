namespace ColorMatch.Domain.Rules;

/// <summary>Level maths with the default balance. Code that has access to configuration should use <see cref="LevelCurve"/> instead.</summary>
public static class LevelRules
{
    public const int XpPerLevelBase = 100;

    /// <summary>Level n starts at 100·(n−1)² XP: 1→0, 2→100, 3→400, 4→900 …</summary>
    public static int LevelForXp(int xp) => LevelCurve.Default.LevelForXp(xp);

    public static int XpForLevel(int level) => LevelCurve.Default.XpForLevel(level);
}
