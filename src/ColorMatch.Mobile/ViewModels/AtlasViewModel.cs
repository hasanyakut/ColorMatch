using System.Collections.ObjectModel;
using ColorMatch.Mobile.Controls;
using ColorMatch.Mobile.Models;
using ColorMatch.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ColorMatch.Mobile.ViewModels;

/// <summary>Lets another screen (the hunt result) open the atlas on a given collection.</summary>
public static class AtlasNav
{
    public static string? FocusCollection { get; set; }
}

/// <summary>One of the five collection chips.</summary>
public sealed partial class AtlasTab(AtlasCollection data) : ObservableObject
{
    private static readonly Dictionary<string, string> Accents = new()
    {
        ["forest"] = "#3FA34D", ["ocean"] = "#1A73D1", ["sunset"] = "#F26BA3", ["desert"] = "#E8B84A", ["neon"] = "#8F2BFF",
    };

    public AtlasCollection Data { get; private set; } = data;
    [ObservableProperty] private bool isSelected;
    public string Key => Data.Key;
    public string Name => Data.Name;
    public string CountText => $"{Data.Unlocked}/{Data.Total}";
    public string AccentHex => Accents.GetValueOrDefault(Data.Key, "#7FB0FF");
    public bool Completed => Data.Completed;
    public void Update(AtlasCollection d) { Data = d; OnPropertyChanged(nameof(CountText)); OnPropertyChanged(nameof(Completed)); }
}

/// <summary>One colour tile: unlocked (shows the colour), locked (padlock) or locked-but-today (an outlined hint of the colour).</summary>
public sealed class AtlasTile(AtlasColor data)
{
    public AtlasColor Data { get; } = data;
    public string Key => Data.Key;
    public bool Unlocked => Data.Unlocked;
    public bool Locked => !Data.Unlocked;
    public bool IsTodayLocked => !Data.Unlocked && Data.IsTodaysHunt && Data.Hex is not null;
    public bool IsPlainLocked => !Data.Unlocked && !IsTodayLocked;
    public string Hex => Data.Hex ?? "#888888";
    public string Name => Data.Name ?? "";
    public Brush RingBrush => new SolidColorBrush(ColorHelper.FromHex(Data.Hex));
    public string Description => Data switch
    {
        { Unlocked: true } => $"{Data.Name} · {Data.Hex}" + (Data.BestScore is { } s ? $" · en iyi {s}" : ""),
        { IsTodaysHunt: true, Hex: not null } => $"{Data.Name} bugün {Difficulty} avda. Fotoğrafla ve aç.",
        _ => $"Kilitli · {Difficulty} av renklerinden biri. Günlük avlarda sırası gelince açılır.",
    };
    private string Difficulty => Data.Difficulty switch { "Easy" => "kolay", "Medium" => "orta", _ => "zor" };
}

public partial class AtlasViewModel(IApiClient api) : BaseViewModel
{
    private AtlasData? _data;
    public ObservableCollection<AtlasTab> Tabs { get; } = [];
    public ObservableCollection<AtlasTile> Tiles { get; } = [];

    [ObservableProperty] private AtlasTab? selected;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(GridHeight))] private double tileSize = 76;
    // the wrapping layout reports a too tall measure inside a scroll view, so its height is stated explicitly: rows of four tiles
    public double GridHeight => Math.Ceiling(Tiles.Count / 4.0) * (TileSize + 8);
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowSkeleton))] private bool loaded;
    [ObservableProperty] private string summaryText = "";
    [ObservableProperty] private string collectionName = "";
    [ObservableProperty] private string collectionDescription = "";
    [ObservableProperty] private string progressText = "";
    [ObservableProperty] private double collectionProgress;
    [ObservableProperty] private string rewardText = "";
    [ObservableProperty] private bool collectionDone;
    [ObservableProperty] private string detailText = "Bir renge dokun.";

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
            _data = await api.GetAtlasAsync();
            foreach (var c in _data.Collections)
            {
                var tab = Tabs.FirstOrDefault(t => t.Key == c.Key);
                if (tab is null) Tabs.Add(new AtlasTab(c)); else tab.Update(c);
            }
            SummaryText = $"{_data.Unlocked} / {_data.Total} renk";
            var focus = AtlasNav.FocusCollection ?? Selected?.Key;
            AtlasNav.FocusCollection = null;
            Select(Tabs.FirstOrDefault(t => t.Key == focus) ?? Tabs.FirstOrDefault());
            Loaded = true;
        }, showBusy: !Loaded);
    }

    [RelayCommand]
    private void Select(AtlasTab? tab)
    {
        if (tab is null || _data is null) return;
        foreach (var t in Tabs) t.IsSelected = ReferenceEquals(t, tab);
        Selected = tab;
        var c = _data.Collections.First(x => x.Key == tab.Key);
        CollectionName = c.Name;
        CollectionDescription = c.Description;
        ProgressText = $"{c.Unlocked} / {c.Total}";
        CollectionProgress = c.Total == 0 ? 0 : (double)c.Unlocked / c.Total;
        CollectionDone = c.Completed;
        RewardText = c.Completed
            ? (c.CompletionXpEarned ? $"Koleksiyon tamamlandı · +{c.CompletionXp} XP kazanıldı" : "Koleksiyon tamamlandı")
            : $"Tamamlayınca +{c.CompletionXp} XP";
        if (Changed("tiles", c))
        {
            Tiles.Clear();
            foreach (var col in c.Colors) Tiles.Add(new AtlasTile(col));
            OnPropertyChanged(nameof(GridHeight));
        }
        DetailText = "Bir renge dokun.";
    }

    [RelayCommand]
    private void ShowTile(AtlasTile? tile)
    {
        if (tile is not null) DetailText = tile.Description;
    }
}
