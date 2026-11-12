using ColorMatch.Mobile.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ColorMatch.Mobile.ViewModels;

/// <summary>
/// One of today's three hunts as the UI shows it (Home rows, Hunt selector). The instance is updated in place when the server state
/// changes, so a row whose hunt just got completed can animate instead of being rebuilt.
/// </summary>
public sealed partial class HuntTaskItem(DailyHunt hunt) : ObservableObject
{
    public DailyHunt Data { get; private set; } = hunt;

    [ObservableProperty] private bool isCompleted = hunt.Completed;
    [ObservableProperty] private bool isSelected;

    public int Id => Data.Id;
    public string Hex => Data.Hex;
    public string Name => Data.Name;
    public int BaseXp => Data.BaseXp;
    public int BestScore => Data.BestScore;
    public bool HasScore => Data.Attempts > 0;

    /// <summary>1 = easy, 2 = medium, 3 = hard (drives the difficulty meter).</summary>
    public int Level => Data.Difficulty switch { "Easy" => 1, "Medium" => 2, _ => 3 };
    public string DifficultyText => Level switch { 1 => "Kolay", 2 => "Orta", _ => "Zor" };
    public string XpText => $"+{Data.BaseXp} XP";
    public string SubText => $"{Data.Hex} · {DifficultyText}";
    public string ScoreText => HasScore ? Data.BestScore.ToString() : "";

    public string StatusText => Data.Completed
        ? $"Tamamlandı · en iyi {Data.BestScore}"
        : HasScore ? $"En iyi {Data.BestScore} · {Data.MinScore}+ gerekli" : "Henüz denenmedi";

    public string TierText => Data.BestTier switch
    {
        "Perfect" => "Kusursuz",
        "Excellent" => "Mükemmel",
        "Great" => "Harika",
        "Good" => "İyi",
        "NeedsImprovement" => "Geliştirilebilir",
        _ => "",
    };

    public void Update(DailyHunt hunt)
    {
        Data = hunt;
        IsCompleted = hunt.Completed;
        foreach (var n in new[] { nameof(Hex), nameof(Name), nameof(BaseXp), nameof(BestScore), nameof(HasScore), nameof(Level), nameof(DifficultyText),
                                  nameof(XpText), nameof(SubText), nameof(ScoreText), nameof(StatusText), nameof(TierText) })
            OnPropertyChanged(n);
    }
}
