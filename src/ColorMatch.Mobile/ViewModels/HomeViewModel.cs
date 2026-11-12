using System.Collections.ObjectModel;
using ColorMatch.Mobile.Controls;
using ColorMatch.Mobile.Models;
using ColorMatch.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ColorMatch.Mobile.ViewModels;

/// <summary>One day of the week strip: empty, partly done (1–2 hunts) or a full day (all three).</summary>
public sealed class WeekDayItem(WeekDay d, int index)
{
    public int Index => index;
    private static readonly string[] Names = ["Paz", "Pzt", "Sal", "Çar", "Per", "Cum", "Cmt"];
    public string Name => Names[(int)d.Date.DayOfWeek];
    public bool IsToday => d.IsToday;
    public bool Full => d.Completed >= d.Total;
    public bool Partial => d.Completed > 0 && !Full;
    public bool Empty => d.Completed == 0;
    public string CountText => d.Completed.ToString();
    public Color Fill => Full ? Color.FromArgb("#C6F432") : Partial ? Color.FromArgb("#3FE0B5") : Colors.Transparent;
    public double Ring => Empty ? (d.IsToday ? 2.2 : 1.4) : 0;
    public string NameFont => d.IsToday ? "SansSemibold" : "SansRegular";
    public string Description => $"{d.Date:dd MMMM}: {d.Completed} / {d.Total} av" + (d.IsToday ? ", bugün" : "");
}

public partial class HomeViewModel(IGameService game, IApiClient api, ISessionService session) : BaseViewModel
{
    private DailyHuntsState? _today;

    [ObservableProperty] private UserProfile? profile;
    [ObservableProperty] private string countdown = "";
    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private bool loaded;

    public ObservableCollection<PostItemViewModel> Recent { get; } = [];
    public ObservableCollection<PostItemViewModel> Collages { get; } = [];
    [ObservableProperty] private int pendingRequests;

    public bool HasPendingRequests => PendingRequests > 0;
    public string PendingText => PendingRequests > 9 ? "9+" : PendingRequests.ToString();
    public bool HasCollages => Collages.Count > 0;
    public bool NoCommunity => Loaded && Recent.Count == 0 && Collages.Count == 0;
    public string? AvatarUrl => api.ToAbsoluteUrl(Profile?.AvatarUrl);
    public string AvatarName => Profile?.UserName ?? "";

    // ---- today's three hunts ----
    public ObservableCollection<HuntTaskItem> Hunts { get; } = [];
    public ObservableCollection<WeekDayItem> Week { get; } = [];
    [ObservableProperty] private string weekText = "";
    [ObservableProperty] private bool hasWeek;
    public bool HasHunts => Hunts.Count > 0;
    public int CompletedCount => _today?.Progress.Completed ?? 0;
    public int HuntTotal => _today?.Progress.Total ?? 3;
    public string ProgressCaption => $"{CompletedCount} / {HuntTotal} tamamlandı";
    public bool AllCompleted => _today?.Progress.AllCompleted == true;
    public bool Seg0Done => Hunts.Count > 0 && Hunts[0].IsCompleted;
    public bool Seg1Done => Hunts.Count > 1 && Hunts[1].IsCompleted;
    public bool Seg2Done => Hunts.Count > 2 && Hunts[2].IsCompleted;
    public string BonusText => _today is null ? "" : _today.Progress.BonusEarned
        ? $"Günlük bonus kazanıldı · +{_today.Progress.CompletionBonusXp} XP"
        : $"Üçünü de tamamla: +{_today.Progress.CompletionBonusXp} XP bonus";
    public string XpTodayText => _today is null ? "" : $"{_today.Progress.XpEarnedToday} / {_today.Progress.XpAvailableToday} XP";

    public ObservableCollection<LeaderRow> Leaders { get; } = [];
    public ObservableCollection<PastRow> Past { get; } = [];
    [ObservableProperty] private string rankText = "";
    [ObservableProperty] private bool hasLeaders;
    [ObservableProperty] private bool hasPast;

    public int Streak => Profile?.StreakDays ?? 0;
    public bool HasStreak => Streak > 0;
    public string StreakText => Streak switch { 0 => "Seri başlat", 1 => "1 gün", _ => $"{Streak} gün" };

    [RelayCommand] private async Task OpenCreateAsync() => await Shell.Current.GoToAsync("//create");
    [RelayCommand] private async Task OpenProfileAsync() => await Shell.Current.GoToAsync("//me");
    [RelayCommand] private async Task OpenFriendsAsync() => await Shell.Current.GoToAsync("friends");
    [RelayCommand] private async Task OpenAtlasAsync() => await Shell.Current.GoToAsync("//atlas");
    [ObservableProperty] private string atlasText = "Renk Atlası";
    [ObservableProperty] private string atlasSub = "";
    [ObservableProperty] private double atlasProgress;
    [RelayCommand] private async Task OpenBingoAsync() => await Shell.Current.GoToAsync("bingo");
    [ObservableProperty] private string bingoText = "Renk Bingosu";
    [ObservableProperty] private string bingoSub = "";
    [ObservableProperty] private double bingoProgress;
    [RelayCommand] private async Task OpenDuelsAsync() => await Shell.Current.GoToAsync("duels");
    [ObservableProperty] private string duelSub = "Arkadaşına meydan oku";
    [RelayCommand]
    private async Task OpenUserAsync(LeaderRow row)
        => await Shell.Current.GoToAsync("profile", new Dictionary<string, object> { ["UserId"] = row.UserId });

    public string Greeting
    {
        get
        {
            var h = DateTime.Now.Hour;
            var hello = h < 5 ? "İyi geceler" : h < 12 ? "Günaydın" : h < 18 ? "İyi günler" : "İyi akşamlar";
            return Profile is null ? hello : $"{hello}, {Profile.UserName}";
        }
    }
    public string DateText => DateTime.Now.ToString("dddd, d MMMM", new System.Globalization.CultureInfo("tr-TR"));
    public int LevelNumber => Profile?.Level ?? 1;
    public bool ShowSkeleton => IsBusy && !Loaded;

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(IsBusy) or nameof(Loaded)) OnPropertyChanged(nameof(ShowSkeleton));
    }
    public double XpProgress => Profile is null ? 0 : Math.Clamp((double)(Profile.TotalXp - Profile.LevelStartXp) / Math.Max(1, Profile.NextLevelXp - Profile.LevelStartXp), 0, 1);
    public string XpText => Profile is null ? "" : $"{Profile.TotalXp} / {Profile.NextLevelXp} XP";
    public string LevelText => Profile is null ? "" : string.IsNullOrEmpty(Profile.Title) ? $"Seviye {Profile.Level}" : Profile.Title;
    public bool HasRecent => Recent.Count > 0;

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsRefreshing = true;
        await GuardAsync(async () =>
        {
            var huntsTask = game.GetHuntsAsync(force: true);
            var meTask = api.GetMeAsync();
            Task<Paged<Post>>? feedTask = api.GetFeedAsync(null, 20);
            await Task.WhenAll(huntsTask, meTask);
            ApplyHunts(huntsTask.Result);
            Profile = meTask.Result;
            session.UpdateUser(Profile);
            try
            {
                var feed = await feedTask;
                if (Changed("feed", feed.Items))
                {
                    Recent.Clear(); Collages.Clear();
                    foreach (var p in feed.Items.Where(p => p.MediaUrls.Count > 0))
                    {
                        var item = new PostItemViewModel(p, api);
                        if (item.IsCollage) { if (Collages.Count < 8) Collages.Add(item); }
                        else if (Recent.Count < 8) Recent.Add(item);
                    }
                }
            }
            catch (ApiException) { /* previews are optional */ }
            try { PendingRequests = (await api.GetFriendRequestsAsync()).Incoming.Count; }
            catch (ApiException) { PendingRequests = 0; }
            await LoadExtrasAsync();
            Loaded = true;
        }, showBusy: !Loaded);
        IsRefreshing = false;
        NotifyAll();
        UpdateCountdown();
    }

    /// <summary>Updates the rows in place (so a newly completed hunt animates) and adds missing ones.</summary>
    private void ApplyHunts(DailyHuntsState state)
    {
        _today = state;
        foreach (var h in state.Hunts)
        {
            var existing = Hunts.FirstOrDefault(x => x.Id == h.Id);
            if (existing is null) Hunts.Add(new HuntTaskItem(h)); else existing.Update(h);
        }
        for (var i = Hunts.Count - 1; i >= 0; i--)
            if (state.Hunts.All(h => h.Id != Hunts[i].Id)) Hunts.RemoveAt(i);   // a new game day replaced yesterday's hunts
    }

    private async Task LoadExtrasAsync()
    {
        try
        {
            var boardTask = api.GetLeaderboardAsync(5);
            var pastTask = api.GetRecentAsync(6);
            await Task.WhenAll(boardTask, pastTask);

            var board = boardTask.Result;
            var top = Math.Max(1, board.Entries.Count == 0 ? 1 : board.Entries.Max(e => e.BestScore));
            if (Changed("leaders", board))
            {
                Leaders.Clear();
                foreach (var e in board.Entries)
                    Leaders.Add(new LeaderRow(e.UserId, e.Rank, e.UserName, api.ToAbsoluteUrl(e.AvatarUrl), e.BestScore, e.IsMe, e.Rank.ToString(), e.BestScore / (double)top));
            }
            HasLeaders = Leaders.Count > 0;
            RankText = board.MyRank is { } r ? $"Senin sıran #{r} · {board.TotalPlayers} oyuncu" : (board.TotalPlayers > 0 ? $"{board.TotalPlayers} oyuncu bugün oynadı" : "");

            if (Changed("past", pastTask.Result))
            {
                Past.Clear();
                foreach (var p in pastTask.Result)
                    Past.Add(new PastRow(p.Hex, p.Name, p.Date.ToString("d MMM", new System.Globalization.CultureInfo("tr-TR")), p.MyBestScore > 0 ? p.MyBestScore.ToString() : "–", p.MyBestScore > 0));
            }
            HasPast = Past.Count > 0;
        }
        catch (ApiException) { /* optional sections stay hidden */ }
        try
        {
            var atlas = await api.GetAtlasAsync();
            AtlasText = $"Renk Atlası · {atlas.Unlocked} / {atlas.Total}";
            AtlasProgress = atlas.Total == 0 ? 0 : (double)atlas.Unlocked / atlas.Total;
            var next = atlas.Collections.Where(c => !c.Completed).OrderByDescending(c => c.Unlocked).FirstOrDefault();
            AtlasSub = next is null ? "Tüm koleksiyonlar tamamlandı" : $"{next.Name}: {next.Unlocked} / {next.Total} · tamamlayınca +{next.CompletionXp} XP";
        }
        catch (ApiException) { AtlasSub = ""; }
        try
        {
            var bingo = await api.GetBingoAsync();
            BingoText = $"Renk Bingosu · {bingo.CompletedCells} / 9";
            BingoProgress = bingo.CompletedCells / 9.0;
            BingoSub = $"{bingo.CompletedLines} / 8 çizgi · çizgi başı +{bingo.LineXp} XP";
        }
        catch (ApiException) { BingoSub = ""; }
        try
        {
            var duels = await api.GetDuelsAsync();
            var invites = duels.Duels.Count(d => d.NeedsMyResponse);
            var play = duels.Duels.Count(d => d.CanPlay);
            DuelSub = invites > 0 ? $"{invites} davet seni bekliyor" : play > 0 ? $"{play} düello oynanmayı bekliyor" : "Arkadaşına meydan oku";
        }
        catch (ApiException) { DuelSub = "Arkadaşına meydan oku"; }
        try
        {
            var week = await api.GetWeekAsync();
            if (Changed("week", week))
            {
                Week.Clear();
                for (var i = 0; i < week.Days.Count; i++) Week.Add(new WeekDayItem(week.Days[i], i));
            }
            WeekText = week.ActiveDays == 0 ? "Bu hafta henüz av tamamlamadın"
                : $"Son 7 günde {week.ActiveDays} gün oynadın" + (week.PerfectDays > 0 ? $" · {week.PerfectDays} tam gün" : "");
            HasWeek = true;
        }
        catch (ApiException) { HasWeek = false; }
    }

    private void NotifyAll()
    {
        foreach (var n in new[]
        {
            nameof(Greeting), nameof(XpProgress), nameof(XpText), nameof(LevelNumber), nameof(DateText), nameof(LevelText),
            nameof(HasPendingRequests), nameof(PendingText), nameof(HasCollages), nameof(NoCommunity), nameof(AvatarUrl), nameof(AvatarName),
            nameof(HasHunts), nameof(CompletedCount), nameof(HuntTotal), nameof(ProgressCaption), nameof(AllCompleted), nameof(BonusText), nameof(XpTodayText),
            nameof(Seg0Done), nameof(Seg1Done), nameof(Seg2Done),
            nameof(HasRecent), nameof(Streak), nameof(HasStreak), nameof(StreakText),
        }) OnPropertyChanged(n);
    }

    public void UpdateCountdown()
    {
        if (_today is null) { Countdown = ""; return; }
        var left = _today.ResetsAtUtc - DateTime.UtcNow;
        if (left <= TimeSpan.Zero) { Countdown = "Yeni avlar hazır!"; return; }
        Countdown = $"Yeni avlara {(int)left.TotalHours:00}:{left.Minutes:00}:{left.Seconds:00}";
    }

    /// <summary>Opens the Hunt tab on the tapped hunt.</summary>
    [RelayCommand]
    private async Task OpenHuntAsync(HuntTaskItem? item)
    {
        game.PendingHuntId = item?.Id;
        await Shell.Current.GoToAsync("//hunt");
    }

    [RelayCommand] private async Task OpenFeedAsync() => await Shell.Current.GoToAsync("//feed");
    [RelayCommand]
    private async Task OpenPostAsync(PostItemViewModel item)
        => await Shell.Current.GoToAsync("post", new ShellNavigationQueryParameters { ["Item"] = item });
}
