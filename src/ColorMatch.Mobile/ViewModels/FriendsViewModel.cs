using System.Collections.ObjectModel;
using ColorMatch.Mobile.Controls;
using ColorMatch.Mobile.Models;
using ColorMatch.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ColorMatch.Mobile.ViewModels;

/// <summary>A colour chip in the discovery filter row.</summary>
public partial class FacetItem(string family, string hex, int count) : ObservableObject
{
    public string Family { get; } = family;
    public string Hex { get; } = hex;
    public int Count { get; } = count;
    public string CountText => Count.ToString();
    [ObservableProperty] private bool isSelected;
}

/// <summary>A person in a friends list / search result, with the actions that fit the current relationship.</summary>
public partial class PersonRow : ObservableObject
{
    public PersonRow(Guid userId, string userName, string? avatarUrl, int level, string relationship)
    {
        UserId = userId; UserName = userName; AvatarUrl = avatarUrl; Level = level; this.relationship = relationship;
    }

    public Guid UserId { get; }
    public string UserName { get; }
    public string? AvatarUrl { get; }
    public int Level { get; }
    public string SubText => $"Seviye {Level}";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(PrimaryText)), NotifyPropertyChangedFor(nameof(PrimaryEnabled)), NotifyPropertyChangedFor(nameof(PrimaryIsAccent)), NotifyPropertyChangedFor(nameof(PrimaryKind)),
        NotifyPropertyChangedFor(nameof(HasPrimary)), NotifyPropertyChangedFor(nameof(SecondaryText)), NotifyPropertyChangedFor(nameof(HasSecondary))]
    private string relationship;

    public bool HasPrimary => Relationship is not ("self" or "friends");
    public string PrimaryText => Relationship switch
    {
        "none" => "Ekle",
        "request_sent" => "İstek gönderildi",
        "request_received" => "Kabul et",
        "friends" => "Arkadaş",
        _ => ""
    };
    public bool PrimaryEnabled => Relationship is "none" or "request_received";
    public bool PrimaryIsAccent => PrimaryEnabled;
    public string PrimaryKind => PrimaryEnabled ? "primary" : "secondary";

    public bool HasSecondary => Relationship is "request_sent" or "request_received" or "friends";
    public string SecondaryText => Relationship switch { "request_sent" => "Geri al", "request_received" => "Reddet", "friends" => "Çıkar", _ => "" };
}

public partial class FriendsViewModel(IApiClient api) : BaseViewModel
{
    private CancellationTokenSource? _searchCts;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsFriendsTab)), NotifyPropertyChangedFor(nameof(IsRequestsTab)), NotifyPropertyChangedFor(nameof(IsSearchTab))]
    private string tab = "friends";
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private bool isSearching;
    [ObservableProperty] private string? infoMessage;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(RequestsLabel)), NotifyPropertyChangedFor(nameof(NoRequests))] private int incomingCount;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(NoFriends))] private bool friendsLoaded;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(SearchHint))] private bool searched;

    public ObservableCollection<PersonRow> Friends { get; } = [];
    public ObservableCollection<PersonRow> Incoming { get; } = [];
    public ObservableCollection<PersonRow> Outgoing { get; } = [];
    public ObservableCollection<PersonRow> Results { get; } = [];

    public bool IsFriendsTab => Tab == "friends";
    public bool IsRequestsTab => Tab == "requests";
    public bool IsSearchTab => Tab == "search";
    public bool NoFriends => FriendsLoaded && Friends.Count == 0;
    public bool NoRequests => FriendsLoaded && Incoming.Count == 0 && Outgoing.Count == 0;
    public string RequestsLabel => IncomingCount > 0 ? $"İstekler ({IncomingCount})" : "İstekler";
    public string SearchHint => !Searched ? "Kullanıcı adının ilk harflerini yaz (en az 2 karakter)." : Results.Count == 0 ? "Kimse bulunamadı." : "";
    public bool HasSearchHint => SearchHint.Length > 0;

    [RelayCommand] private void SetTab(string value) => Tab = value;

    [RelayCommand]
    public async Task LoadAsync()
    {
        await GuardAsync(async () =>
        {
            var friendsTask = api.GetFriendsAsync();
            var requestsTask = api.GetFriendRequestsAsync();
            await Task.WhenAll(friendsTask, requestsTask);

            Fill(Friends, friendsTask.Result.Select(f => new PersonRow(f.UserId, f.UserName, api.ToAbsoluteUrl(f.AvatarUrl), f.Level, "friends")));
            Fill(Incoming, requestsTask.Result.Incoming.Select(f => new PersonRow(f.UserId, f.UserName, api.ToAbsoluteUrl(f.AvatarUrl), f.Level, "request_received")));
            Fill(Outgoing, requestsTask.Result.Outgoing.Select(f => new PersonRow(f.UserId, f.UserName, api.ToAbsoluteUrl(f.AvatarUrl), f.Level, "request_sent")));
            IncomingCount = Incoming.Count;
            FriendsLoaded = true;
            OnPropertyChanged(nameof(NoFriends));
            OnPropertyChanged(nameof(NoRequests));
        }, showBusy: !FriendsLoaded);
    }

    private static void Fill(ObservableCollection<PersonRow> target, IEnumerable<PersonRow> rows)
    {
        target.Clear();
        foreach (var r in rows) target.Add(r);
    }

    // ---- search (debounced) ----
    partial void OnSearchTextChanged(string value)
    {
        _searchCts?.Cancel();
        var q = (value ?? "").Trim();
        if (q.Length < 2)
        {
            Results.Clear();
            Searched = false;
            OnPropertyChanged(nameof(HasSearchHint));
            return;
        }
        var cts = _searchCts = new CancellationTokenSource();
        _ = SearchAsync(q, cts.Token);
    }

    private async Task SearchAsync(string query, CancellationToken ct)
    {
        try
        {
            await Task.Delay(350, ct);
            IsSearching = true;
            ErrorMessage = null;
            var found = await api.SearchUsersAsync(query, ct);
            if (ct.IsCancellationRequested) return;
            Fill(Results, found.Select(u => new PersonRow(u.UserId, u.UserName, api.ToAbsoluteUrl(u.AvatarUrl), u.Level, u.Relationship)));
            Searched = true;
            OnPropertyChanged(nameof(SearchHint));
            OnPropertyChanged(nameof(HasSearchHint));
        }
        catch (OperationCanceledException) { }
        catch (ApiException ex) { ErrorMessage = ex.Message; }
        finally { if (!ct.IsCancellationRequested) IsSearching = false; }
    }

    // ---- actions ----
    [RelayCommand]
    private async Task PrimaryAsync(PersonRow row)
    {
        InfoMessage = null;
        try
        {
            var rel = row.Relationship switch
            {
                "none" => await api.SendFriendRequestAsync(row.UserId),
                "request_received" => await api.AcceptFriendRequestAsync(row.UserId),
                _ => null
            };
            if (rel is null) return;
            row.Relationship = rel.Relationship;
            InfoMessage = rel.Relationship == "friends" ? $"{row.UserName} artık arkadaşın." : $"{row.UserName} kişisine istek gönderildi.";
            await ReloadListsAsync();
        }
        catch (ApiException ex) { InfoMessage = ex.Message; }
    }

    [RelayCommand]
    private async Task SecondaryAsync(PersonRow row)
    {
        InfoMessage = null;
        try
        {
            if (row.Relationship == "friends")
            {
                var page = Application.Current?.Windows.FirstOrDefault()?.Page;
                if (page is null || !await page.DisplayAlertAsync("Arkadaşlıktan çıkar", $"{row.UserName} arkadaş listenden çıkarılsın mı?", "Çıkar", "Vazgeç")) return;
            }
            var rel = await api.RemoveFriendAsync(row.UserId);
            row.Relationship = rel.Relationship;
            await ReloadListsAsync();
        }
        catch (ApiException ex) { InfoMessage = ex.Message; }
    }

    private async Task ReloadListsAsync()
    {
        try
        {
            var friendsTask = api.GetFriendsAsync();
            var requestsTask = api.GetFriendRequestsAsync();
            await Task.WhenAll(friendsTask, requestsTask);
            Fill(Friends, friendsTask.Result.Select(f => new PersonRow(f.UserId, f.UserName, api.ToAbsoluteUrl(f.AvatarUrl), f.Level, "friends")));
            Fill(Incoming, requestsTask.Result.Incoming.Select(f => new PersonRow(f.UserId, f.UserName, api.ToAbsoluteUrl(f.AvatarUrl), f.Level, "request_received")));
            Fill(Outgoing, requestsTask.Result.Outgoing.Select(f => new PersonRow(f.UserId, f.UserName, api.ToAbsoluteUrl(f.AvatarUrl), f.Level, "request_sent")));
            IncomingCount = Incoming.Count;
            OnPropertyChanged(nameof(NoFriends));
            OnPropertyChanged(nameof(NoRequests));
        }
        catch (ApiException) { }
    }

    [RelayCommand]
    private async Task OpenProfileAsync(PersonRow row)
        => await Shell.Current.GoToAsync("profile", new Dictionary<string, object> { ["UserId"] = row.UserId });

    [RelayCommand] private async Task BackAsync() => await Shell.Current.GoToAsync("..");
}
