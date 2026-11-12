using System.Collections.ObjectModel;
using ColorMatch.Mobile.Controls;
using ColorMatch.Mobile.Models;
using ColorMatch.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ColorMatch.Mobile.ViewModels;

/// <summary>One duel row with the wording and the actions that fit its state.</summary>
public sealed class DuelItem(Duel d)
{
    public Duel Data { get; } = d;
    public Guid Id => Data.Id;
    public string Title => $"{Data.OtherUserName} ile düello";
    public string? AvatarUrl => Data.OtherAvatarUrl;
    // the invited player does not know the colour before accepting
    public string ColorHex => Data.TargetHex ?? "#8E8E93";
    public bool ColorHidden => Data.TargetHex is null;
    public string ColorText => Data.TargetName ?? "Renk gizli";

    private bool Over => Data.Outcome is not null;
    public bool IsOpen => Data.Status is "Pending" or "Active";

    public string StatusText => Data.Status switch
    {
        "Pending" when Data.NeedsMyResponse => "Seni düelloya çağırdı",
        "Pending" => "Yanıt bekleniyor",
        "Declined" => "Reddedildi",
        "Active" when Data.CanPlay => $"{Data.MyAttemptsLeft} hakkın var" + (Data.MyBest is { } b ? $" · en iyin {b}" : ""),
        "Active" when Data.MyDone => "Sen bitirdin · rakibin bitirmesi bekleniyor",
        "Active" => "Süre doldu, sonuç hesaplanıyor",
        _ when Data.Outcome == "win" => $"Kazandın" + (Data.MyXp > 0 ? $" · +{Data.MyXp} XP" : ""),
        _ when Data.Outcome == "lose" => "Kaybettin" + (Data.MyXp > 0 ? $" · +{Data.MyXp} XP" : ""),
        _ when Data.Outcome == "draw" => "Berabere" + (Data.MyXp > 0 ? $" · +{Data.MyXp} XP" : ""),
        _ when Data.Outcome == "none" => "Kimse oynamadı",
        _ => "Süresi doldu",
    };

    /// <summary>Scores are only shown for the player's own attempts while the duel runs; the other side appears once it is over.</summary>
    public string ScoreText => Over
        ? $"Sen {(Data.MyBest?.ToString() ?? "–")} · {Data.OtherUserName} {(Data.OtherBest?.ToString() ?? "–")}"
        : Data.Status == "Active"
            ? $"Sen {(Data.MyBest?.ToString() ?? "–")} · {Data.OtherUserName} {(Data.OtherDone ? "bitirdi" : $"{Data.OtherAttempts}/{Data.AttemptsPerPlayer} deneme")} (skor gizli)"
            : "";
    public bool HasScoreText => ScoreText.Length > 0;

    public string TimeText
    {
        get
        {
            if (!IsOpen) return "";
            var left = Data.ExpiresAtUtc - DateTime.UtcNow;
            if (left <= TimeSpan.Zero) return "Süre doldu";
            var what = Data.Status == "Pending" ? "Davet" : "Süre";
            return left.TotalHours >= 1 ? $"{what}: {(int)left.TotalHours} sa {left.Minutes} dk kaldı" : $"{what}: {Math.Max(1, left.Minutes)} dk kaldı";
        }
    }
    public bool HasTimeText => TimeText.Length > 0;

    public bool ShowRespond => Data.NeedsMyResponse;
    public bool ShowWithdraw => Data.Status == "Pending" && Data.Role == "challenger";
    public bool ShowPlay => Data.CanPlay;
    public bool ShowFinish => Data.Status == "Active" && !Data.MyDone && Data.MyScores.Count > 0;
    /// <summary>A finished (or declined / expired) duel can be played again with one tap.</summary>
    public bool ShowRematch => !IsOpen;
    public string PlayText => Data.MyScores.Count == 0 ? "Oyna" : "Devam et";
    public string Description => $"{Title}. {StatusText}. {ScoreText}";
}

public partial class DuelsViewModel(IApiClient api, IGameService game, ISessionService session) : BaseViewModel
{
    public ObservableCollection<DuelItem> Duels { get; } = [];
    public ObservableCollection<Friend> Friends { get; } = [];

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowSkeleton)), NotifyPropertyChangedFor(nameof(ShowEmpty))] private bool loaded;
    [ObservableProperty] private bool showPicker;
    [ObservableProperty] private string? infoMessage;
    [ObservableProperty] private string summaryText = "";
    [ObservableProperty] private string rulesText = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(NoFriendsText))] private bool friendsLoaded;

    public bool ShowSkeleton => IsBusy && !Loaded;
    public bool ShowEmpty => Loaded && Duels.Count == 0;
    public string NoFriendsText => FriendsLoaded && Friends.Count == 0 ? "Düello için önce arkadaş eklemelisin." : "";
    public bool HasNoFriends => NoFriendsText.Length > 0;

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(IsBusy)) OnPropertyChanged(nameof(ShowSkeleton));
    }

    [RelayCommand] private async Task BackAsync() => await Shell.Current.GoToAsync("..");

    [RelayCommand]
    public async Task LoadAsync()
    {
        await GuardAsync(async () =>
        {
            var list = await api.GetDuelsAsync();
            if (Changed("duels", new { list.Duels, minute = DateTime.UtcNow.ToString("HHmm") }))
            {
                Duels.Clear();
                foreach (var d in list.Duels) Duels.Add(new DuelItem(d));
            }
            SummaryText = $"{list.OpenCount} / {list.MaxOpen} açık düello";
            RulesText = $"Aynı rengi 24 saat içinde, en fazla 3 denemeyle bul. Kazanan +{list.WinXp} XP, berabere +{list.DrawXp} XP, kaybeden +{list.LoseXp} XP. " +
                        "Rakibin skoru düello bitene kadar gizli kalır.";
            Loaded = true;
            OnPropertyChanged(nameof(ShowEmpty));
        }, showBusy: !Loaded);
    }

    [RelayCommand]
    private async Task TogglePickerAsync()
    {
        ShowPicker = !ShowPicker;
        if (!ShowPicker) return;
        InfoMessage = null;
        try
        {
            var friends = await api.GetFriendsAsync();
            Friends.Clear();
            foreach (var f in friends) Friends.Add(f);
            FriendsLoaded = true;
            OnPropertyChanged(nameof(HasNoFriends));
        }
        catch (ApiException ex) { InfoMessage = ex.Message; }
    }

    [RelayCommand]
    private async Task ChallengeAsync(Friend? friend)
    {
        if (friend is null) return;
        InfoMessage = null;
        try
        {
            await api.CreateDuelAsync(friend.UserId);
            ShowPicker = false;
            InfoMessage = $"{friend.UserName} düelloya davet edildi. Kabul edince 24 saatin başlar.";
            await LoadAsync();
        }
        catch (ApiException ex) { InfoMessage = ex.Message; }
    }

    private async Task ActAsync(Func<Task<Duel>> action, string? done = null)
    {
        InfoMessage = null;
        try { await action(); if (done is not null) InfoMessage = done; await LoadAsync(); }
        catch (ApiException ex) { InfoMessage = ex.Message; await LoadAsync(); }
    }

    [RelayCommand] private Task AcceptAsync(DuelItem? d) => d is null ? Task.CompletedTask : ActAsync(() => api.AcceptDuelAsync(d.Id), "Düello başladı: 24 saatin var.");
    [RelayCommand] private Task DeclineAsync(DuelItem? d) => d is null ? Task.CompletedTask : ActAsync(() => api.DeclineDuelAsync(d.Id));

    [RelayCommand]
    private async Task FinishAsync(DuelItem? d)
    {
        if (d is null) return;
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null || !await page.DisplayAlertAsync("Düelloyu bitir", "Kalan denemelerinden vazgeçip en iyi skorunla bitirmek istiyor musun?", "Bitir", "Vazgeç")) return;
        await ActAsync(() => api.FinishDuelAsync(d.Id), "Düellonu bitirdin. Rakibin bitirmesi ya da süre dolması bekleniyor.");
    }

    /// <summary>Challenges the same friend again (a new colour, a new 24 hours).</summary>
    [RelayCommand]
    private async Task RematchAsync(DuelItem? d)
    {
        if (d is null) return;
        InfoMessage = null;
        try
        {
            await api.CreateDuelAsync(d.Data.OtherUserId);
            InfoMessage = $"{d.Data.OtherUserName} rövanşa davet edildi.";
            await LoadAsync();
        }
        catch (ApiException ex) { InfoMessage = ex.Message; }
    }

    /// <summary>Plays in the capture screen (Hunt tab in duel mode).</summary>
    [RelayCommand]
    private async Task PlayAsync(DuelItem? d)
    {
        if (d is null || !d.Data.CanPlay) return;
        game.PendingDuelId = d.Id;
        await Shell.Current.GoToAsync("//hunt");
    }
}
