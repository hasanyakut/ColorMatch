namespace ColorMatch.Domain.Rules;

/// <summary>
/// Total XP ↔ level ↔ title, driven by <see cref="GameBalance"/>. Pure and deterministic.
/// A level starts at <c>round(base · (level−1)^exponent)</c> XP, so every next level costs more than the one before
/// (for exponent &gt; 1).
/// </summary>
public sealed class LevelCurve
{
    public static LevelCurve Default { get; } = new(new GameBalance());

    private readonly int _baseXp;
    private readonly double _exponent;
    private readonly IReadOnlyList<TitleTier> _titles;

    public LevelCurve(GameBalance balance)
    {
        _baseXp = Math.Max(1, balance.LevelBaseXp);
        _exponent = Math.Max(1.0, balance.LevelExponent);
        _titles = balance.EffectiveTitles;
    }

    /// <summary>XP at which <paramref name="level"/> begins (level 1 → 0).</summary>
    public int XpForLevel(int level)
        => level <= 1 ? 0 : (int)Math.Min(int.MaxValue, Math.Round(_baseXp * Math.Pow(level - 1, _exponent), MidpointRounding.AwayFromZero));

    public int LevelForXp(int xp)
    {
        xp = Math.Max(0, xp);
        var level = (int)Math.Floor(Math.Pow(xp / (double)_baseXp, 1.0 / _exponent)) + 1;
        // the closed form can be off by one around rounding boundaries: settle it against the exact thresholds
        while (level > 1 && XpForLevel(level) > xp) level--;
        while (XpForLevel(level + 1) <= xp && level < 10_000) level++;
        return level;
    }

    public string TitleForLevel(int level)
    {
        var title = _titles[0].Name;
        foreach (var tier in _titles)
            if (level >= tier.MinLevel) title = tier.Name;
        return title;
    }
}
