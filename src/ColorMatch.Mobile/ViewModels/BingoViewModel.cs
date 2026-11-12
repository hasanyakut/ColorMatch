using System.Collections.ObjectModel;
using ColorMatch.Mobile.Controls;
using ColorMatch.Mobile.Models;
using ColorMatch.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ColorMatch.Mobile.ViewModels;

/// <summary>One square of the bingo board.</summary>
public sealed class BingoTile(BingoCell data)
{
    public BingoCell Data { get; } = data;
    public int Index => Data.Index;
    public string Name => Data.Name;
    public string Hex => Data.Hex;
    public bool Completed => Data.Completed;
    public string Difficulty => Data.Difficulty switch { "Easy" => "kolay", "Medium" => "orta", _ => "zor" };
    public string Description => Data.Completed
        ? $"{Data.Name} tamamlandı · en iyi {Data.BestScore}"
        : $"{Data.Name} · {Difficulty} renk" + (Data.Attempts > 0 ? $" · en iyi {Data.BestScore} · {Data.AttemptsLeft} hak kaldı" : "");
}

/// <summary>One of the eight lines with its progress.</summary>
public sealed class BingoLineItem(BingoLine line, int done)
{
    public string Name => line.Name;
    public bool Done => line.Completed;
    public string ProgressText => line.Completed ? "Tamamlandı" : $"{done} / 3";
    public string XpText => line.Completed ? "kazanıldı" : $"+{line.Xp} XP";
}

public partial class BingoViewModel(IApiClient api, IGameService game) : BaseViewModel
{
    public ObservableCollection<BingoTile> Tiles { get; } = [];
    public ObservableCollection<BingoLineItem> Lines { get; } = [];

    [ObservableProperty, NotifyPropertyChangedFor(nameof(GridHeight))] private double tileSize = 100;
    // the wrapping layout reports a too tall measure inside a scroll view, so its height is stated explicitly: three rows of three squares
    public double GridHeight => 3 * (TileSize + 8);
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowSkeleton))] private bool loaded;
    [ObservableProperty] private string summaryText = "";
    [ObservableProperty] private string rewardText = "";
    [ObservableProperty] private string boardsText = "";
    [ObservableProperty] private string detailText = "Bir kareye dokun ve o renkte bir şey bul.";

    public bool ShowSkeleton => IsBusy && !Loaded;
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
            var board = await api.GetBingoAsync();
            if (Changed("board", board))
            {
                Tiles.Clear();
                foreach (var c in board.Cells.OrderBy(c => c.Index)) Tiles.Add(new BingoTile(c));
                OnPropertyChanged(nameof(GridHeight));

                Lines.Clear();
                foreach (var l in board.Lines) Lines.Add(new BingoLineItem(l, l.Cells.Count(i => board.Cells.First(c => c.Index == i).Completed)));
            }

            SummaryText = $"{board.CompletedCells} / 9 kare · {board.CompletedLines} / 8 çizgi";
            RewardText = $"Her çizgi +{board.LineXp} XP · tahta bitince +{board.BoardXp} XP";
            BoardsText = board.BoardsCompleted > 0 ? $"Tamamlanan tahta: {board.BoardsCompleted}" : "";
            DetailText = "Bir kareye dokun ve o renkte bir şey bul.";
            Loaded = true;
        }, showBusy: !Loaded);
    }

    /// <summary>An open square starts a hunt for its colour on the Hunt tab; a finished one only shows its details.</summary>
    [RelayCommand]
    private async Task TapTileAsync(BingoTile? tile)
    {
        if (tile is null) return;
        if (tile.Completed) { DetailText = tile.Description; return; }
        game.PendingBingoCell = tile.Index;
        await Shell.Current.GoToAsync("//hunt");
    }
}
