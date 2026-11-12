namespace ColorMatch.Domain.Rules;

/// <summary>
/// XP is derived from the best score of the day. Re-playing the daily colour only pays the
/// difference between the new best reward and what was already paid out, so XP farming is impossible.
/// </summary>
public static class XpRules
{
    /// <summary>Total XP a given best score is worth: score/10 plus tier bonuses.</summary>
    public static int XpForScore(int score)
    {
        if (score < 0) score = 0;
        var bonus = score >= 990 ? 75 : score >= 950 ? 50 : score >= 900 ? 25 : 0;
        return score / 10 + bonus;
    }

    /// <summary>XP to pay now = reward(best after attempt) − XP already paid for this challenge (never negative).</summary>
    public static int XpToAward(int newScore, int xpAlreadyAwardedForChallenge)
        => Math.Max(0, XpForScore(newScore) - xpAlreadyAwardedForChallenge);
}
