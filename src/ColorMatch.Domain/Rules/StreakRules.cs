namespace ColorMatch.Domain.Rules;

/// <summary>The stored streak of a player. <see cref="Last"/> is the game day of the last qualifying activity.</summary>
public readonly record struct StreakState(int Current, int Longest, DateOnly? Last);

public static class StreakRules
{
    /// <summary>
    /// Length of the run of consecutive play days ending today, or ending yesterday if the user has not played yet today
    /// (so the streak is not lost before the day is over).
    /// </summary>
    public static int Compute(IEnumerable<DateOnly> playDays, DateOnly today)
    {
        var set = playDays.ToHashSet();
        var cursor = set.Contains(today) ? today : today.AddDays(-1);
        var count = 0;
        while (set.Contains(cursor)) { count++; cursor = cursor.AddDays(-1); }
        return count;
    }

    /// <summary>
    /// Applies one qualifying activity on game day <paramref name="today"/>:
    /// same day → unchanged (a day counts once); the day after the last one → +1; a gap (or the first ever activity) → 1.
    /// A clock that appears to go backwards never changes the streak.
    /// </summary>
    public static StreakState Advance(StreakState state, DateOnly today)
    {
        int current;
        if (state.Last is not { } last) current = 1;
        else if (today <= last) return state;
        else if (today == last.AddDays(1)) current = state.Current + 1;
        else current = 1;

        return new StreakState(current, Math.Max(state.Longest, current), today);
    }

    /// <summary>
    /// The streak as it should be shown today: it survives until the end of the day after the last activity, afterwards it is 0
    /// (the stored value is only replaced by the next qualifying activity).
    /// </summary>
    public static int Effective(StreakState state, DateOnly today)
        => state.Last is { } last && last >= today.AddDays(-1) ? state.Current : 0;

    /// <summary>Rebuilds a stored state from a set of activity days (used to migrate existing players).</summary>
    public static StreakState FromHistory(IEnumerable<DateOnly> playDays)
    {
        var days = playDays.Distinct().OrderBy(d => d).ToList();
        if (days.Count == 0) return default;

        int run = 0, longest = 0;
        DateOnly? prev = null;
        foreach (var d in days)
        {
            run = prev is { } p && d == p.AddDays(1) ? run + 1 : 1;
            longest = Math.Max(longest, run);
            prev = d;
        }
        return new StreakState(run, longest, days[^1]);
    }
}
