using System.Collections.ObjectModel;
using ColorMatch.Mobile.Controls;
using ColorMatch.Mobile.Models;
using ColorMatch.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ColorMatch.Mobile.ViewModels;

/// <summary>
/// The Challenges tab: everything the player can play right now in one place (daily hunts, the bingo board, friend duels).
/// Each section loads on its own, so one failing request shows a retry inside that section instead of emptying the screen.
/// </summary>
public partial class ChallengesViewModel(IApiClient api, IGameService game) : BaseViewModel
{
    public ObservableCollection<HuntTaskItem> Hunts { get; } = [];
    public ObservableCollection<DuelItem> Duels { get; } = [];

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowSkeleton))] private bool loaded;
    [ObservableProperty] private string summaryText = "";

    // daily hunts
    [ObservableProperty] private string huntsText = "";
    [ObservableProperty] private string? huntsError;
    [ObservableProperty] private double huntsProgress;

    // bingo
    [ObservableProperty] private string bingoText = "";
    [ObservableProperty] private string bingoSub = "";
    [ObservableProperty] private double bingoProgress;
    [ObservableProperty] private string? bingoError;
    [ObservableProperty] private bool hasBingo;

    // duels
    [ObservableProperty] private string duelsText = "";
    [ObservableProperty] private string? duelsError;
    [ObservableProperty] private bool noDuels;

    public bool ShowSkeleton => IsBusy && !Loaded;
    public bool HasHuntsError => !string.IsNullOrEmpty(HuntsError);
    public bool HasBingoError => !string.IsNullOrEmpty(BingoError);
    public bool HasDuelsError => !string.IsNullOrEmpty(DuelsError);
    public bool HasDuels => Duels.Count > 0;

    partial void OnHuntsErrorChanged(string? value) => OnPropertyChanged(nameof(HasHuntsError));
    partial void OnBingoErrorChanged(string? value) => OnPropertyChanged(nameof(HasBingoError));
    partial void OnDuelsErrorChanged(string? value) => OnPropertyChanged(nameof(HasDuelsError));

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(IsBusy)) OnPropertyChanged(nameof(ShowSkeleton));
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        await GuardAsync(async () =>
        {
            var huntsTask = LoadHuntsAsync();
            var bingoTask = LoadBingoAsync();
            var duelsTask = LoadDuelsAsync();
            await Task.WhenAll(huntsTask, bingoTask, duelsTask);

            // every section failed (offline, server down): one clear screen-level message with a retry
            if (HasHuntsError && HasBingoError && HasDuelsError)
            {
                ErrorMessage = HuntsError;
                Loaded = false;
                return;
            }
            Loaded = true;
        }, showBusy: !Loaded);
    }

    private async Task LoadHuntsAsync()
    {
        try
        {
            var state = await game.GetHuntsAsync(force: true);
            if (Changed("hunts", state.Hunts))
            {
                Hunts.Clear();
                foreach (var h in state.Hunts) Hunts.Add(new HuntTaskItem(h));
            }
            var done = state.Hunts.Count(h => h.Completed);
            HuntsText = $"{done} / {state.Hunts.Count} tamamlandı";
            HuntsProgress = state.Hunts.Count == 0 ? 0 : done / (double)state.Hunts.Count;
            HuntsError = null;
        }
        catch (ApiException ex) { HuntsError = ex.Message; }
    }

    private async Task LoadBingoAsync()
    {
        try
        {
            var b = await api.GetBingoAsync();
            BingoText = $"Renk Bingosu · {b.CompletedCells} / 9 kare";
            BingoProgress = b.CompletedCells / 9.0;
            BingoSub = $"{b.CompletedLines} / 8 çizgi · çizgi başı +{b.LineXp} XP";
            HasBingo = true;
            BingoError = null;
        }
        catch (ApiException ex) { BingoError = ex.Message; HasBingo = false; }
    }

    private async Task LoadDuelsAsync()
    {
        try
        {
            var list = await api.GetDuelsAsync();
            // what needs the player first (the server already orders them): invitations, then playable ones, then waiting ones.
            // The minute is part of the signature so "x dk kaldı" stays current without rebuilding on every visit.
            var open = list.Duels.Where(d => d.Status is "Pending" or "Active").Take(3).ToList();
            if (Changed("duels", new { open, minute = DateTime.UtcNow.ToString("HHmm") }))
            {
                Duels.Clear();
                foreach (var d in open) Duels.Add(new DuelItem(d));
            }
            var invites = list.Duels.Count(d => d.NeedsMyResponse);
            DuelsText = $"{list.OpenCount} / {list.MaxOpen} açık düello" + (invites > 0 ? $" · {invites} davet" : "");
            NoDuels = Duels.Count == 0;
            DuelsError = null;
            OnPropertyChanged(nameof(HasDuels));
        }
        catch (ApiException ex) { DuelsError = ex.Message; }
    }

    [RelayCommand]
    private async Task OpenHuntAsync(HuntTaskItem? hunt)
    {
        if (hunt is null) return;
        game.PendingHuntId = hunt.Id;
        TabNav.From = TabNav.Index; TabNav.Direction = 1;
        TabNav.ExitBingo = true;
        await Shell.Current.GoToAsync("//hunt");
    }

    [RelayCommand] private async Task OpenBingoAsync() => await Shell.Current.GoToAsync("bingo");
    [RelayCommand] private async Task OpenDuelsAsync() => await Shell.Current.GoToAsync("duels");

    [RelayCommand]
    private async Task PlayDuelAsync(DuelItem? d)
    {
        if (d is null) return;
        if (d.Data.CanPlay) { game.PendingDuelId = d.Id; await Shell.Current.GoToAsync("//hunt"); }
        else await Shell.Current.GoToAsync("duels");
    }
}
