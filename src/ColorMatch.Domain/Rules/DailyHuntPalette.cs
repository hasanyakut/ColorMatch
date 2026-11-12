using ColorMatch.Domain.Enums;

namespace ColorMatch.Domain.Rules;

/// <summary>
/// The three colours of a game day, chosen on the server as a pure function of the date (every instance and every player agrees).
/// All three lists are the Color Atlas catalogue split by difficulty (<see cref="AtlasCatalog"/>): easy = vivid everyday colours,
/// medium = named shades, hard = muted, easy-to-confuse tones. The lists are disjoint, so a day never shows the same colour twice,
/// and each step is coprime with its list size, so every Atlas colour comes up once per cycle (about 33 days) before any repeats.
/// </summary>
public static class DailyHuntPalette
{
    public static readonly IReadOnlyList<DailyColorPalette.Entry> Easy = Pool(HuntDifficulty.Easy);
    public static readonly IReadOnlyList<DailyColorPalette.Entry> Medium = Pool(HuntDifficulty.Medium);
    public static readonly IReadOnlyList<DailyColorPalette.Entry> Hard = Pool(HuntDifficulty.Hard);

    private static IReadOnlyList<DailyColorPalette.Entry> Pool(HuntDifficulty d)
        => AtlasCatalog.For(d).Select(c => new DailyColorPalette.Entry(c.Name, c.Hex)).ToList();

    // steps are coprime with 34 / 33 / 33; the offsets keep the three lists out of phase
    private const int EasyStep = 5, EasyOffset = 3;
    private const int MediumStep = 7, MediumOffset = 11;
    private const int HardStep = 10, HardOffset = 9;

    public static DailyColorPalette.Entry ForDate(DateOnly date, HuntDifficulty difficulty) => difficulty switch
    {
        HuntDifficulty.Easy => Pick(Easy, date, EasyStep, EasyOffset),
        HuntDifficulty.Medium => Pick(Medium, date, MediumStep, MediumOffset),
        HuntDifficulty.Hard => Pick(Hard, date, HardStep, HardOffset),
        _ => throw new ArgumentOutOfRangeException(nameof(difficulty)),
    };

    private static DailyColorPalette.Entry Pick(IReadOnlyList<DailyColorPalette.Entry> list, DateOnly date, int step, int offset)
        => list[(int)(((long)date.DayNumber * step + offset) % list.Count)];
}
