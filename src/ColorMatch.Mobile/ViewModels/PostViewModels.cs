using System.Collections.ObjectModel;
using ColorMatch.Mobile.Controls;
using ColorMatch.Mobile.Models;
using ColorMatch.Mobile.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ColorMatch.Mobile.ViewModels;

/// <summary>One post as displayed in lists; owns its optimistic like toggle.</summary>
public partial class PostItemViewModel : ObservableObject
{
    private readonly IApiClient _api;

    public PostItemViewModel(Post post, IApiClient api)
    {
        _api = api;
        Id = post.Id;
        AuthorId = post.Author.Id;
        AuthorName = post.Author.UserName;
        AuthorAvatar = api.ToAbsoluteUrl(post.Author.AvatarUrl);
        Caption = post.Caption;
        ColorHex = post.ColorHex;
        ImageUrl = api.ToAbsoluteUrl(post.MediaUrls.FirstOrDefault());
        IsCollage = post.Type == PostType.Collage;
        TimeText = Controls.TimeText.Ago(post.CreatedAtUtc);
        CreatedAtUtc = post.CreatedAtUtc;
        likeCount = post.LikeCount;
        commentCount = post.CommentCount;
        likedByMe = post.LikedByMe;
    }

    public Guid Id { get; }
    public Guid AuthorId { get; }
    public string AuthorName { get; }
    public string? AuthorAvatar { get; }
    public string? Caption { get; }
    public string? ColorHex { get; }
    public string? ImageUrl { get; }
    public bool IsCollage { get; }
    public string TimeText { get; }
    public bool HasCaption => !string.IsNullOrWhiteSpace(Caption);
    public bool HasColor => !string.IsNullOrEmpty(ColorHex);
    public string? ColorFamily => HasColor ? ColorMatch.ColorEngine.ColorAnalysis.ColorFamilies.Classify(ColorHex) : null;
    public IReadOnlyList<Controls.ToneItem> Tones => Controls.ColorTones.For(ColorHex);
    public DateTime CreatedAtUtc { get; }
    public string TypeText => IsCollage ? "Kolaj" : "Fotoğraf";
    public string TypeIcon => IsCollage ? "layers" : "camera";
    public string FullDateText => CreatedAtUtc.ToLocalTime().ToString("d MMMM yyyy, HH:mm", new System.Globalization.CultureInfo("tr-TR"));
    public string RgbText => ColorMatch.ColorEngine.ColorConversion.Rgb.TryParseHex(ColorHex, out var c) ? $"RGB {c.R} · {c.G} · {c.B}" : "";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(LikeText))] private int likeCount;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CommentText))] private int commentCount;
    [ObservableProperty] private bool likedByMe;

    public string LikeText => LikeCount.ToString();
    public string CommentText => CommentCount.ToString();

    public void Apply(Post p) { LikeCount = p.LikeCount; CommentCount = p.CommentCount; LikedByMe = p.LikedByMe; }

    /// <summary>Double-tap on the photo: only ever likes (never un-likes by accident).</summary>
    [RelayCommand]
    private async Task LikeAsync()
    {
        if (!LikedByMe) await ToggleLikeAsync();
    }

    [RelayCommand]
    private async Task ToggleLikeAsync()
    {
        var wanted = !LikedByMe;
        if (wanted) Controls.Motion.Haptic();
        // optimistic update, rolled back on failure
        LikedByMe = wanted;
        LikeCount += wanted ? 1 : -1;
        try
        {
            var state = await _api.SetLikeAsync(Id, wanted);
            LikedByMe = state.Liked;
            LikeCount = state.LikeCount;
        }
        catch (Exception)
        {
            LikedByMe = !wanted;
            LikeCount += wanted ? -1 : 1;
        }
    }
}

public partial class FeedViewModel(IApiClient api, IPostPublisher publisher) : BaseViewModel
{
    private string? _cursor;
    private bool _hasMore = true;

    public ObservableCollection<PostItemViewModel> Items { get; } = [];
    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsEmpty))] private bool loaded;
    [ObservableProperty] private bool isLoadingMore;
    [ObservableProperty] private string? infoMessage;

    // "friends" = me + accepted friends, "all" = discovery (everyone), optionally narrowed to one colour family
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsFriendsTab)), NotifyPropertyChangedFor(nameof(IsDiscoverTab)), NotifyPropertyChangedFor(nameof(EmptyText))]
    private string scope = "friends";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(EmptyText)), NotifyPropertyChangedFor(nameof(NoFamilySelected))] private string? selectedFamily;
    public bool NoFamilySelected => SelectedFamily is null;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasPendingRequests)), NotifyPropertyChangedFor(nameof(PendingText))] private int pendingRequests;

    public ObservableCollection<FacetItem> Facets { get; } = [];
    public bool IsFriendsTab => Scope == "friends";
    public bool IsDiscoverTab => Scope == "all";
    public bool HasPendingRequests => PendingRequests > 0;
    public string PendingText => PendingRequests.ToString();
    public string EmptyText => Scope == "friends"
        ? "Arkadaşlarının gönderileri burada görünür. Arkadaş ekle ya da Keşfet'e göz at!"
        : SelectedFamily is { } f ? $"Henüz {f.ToLower(new System.Globalization.CultureInfo("tr-TR"))} bir paylaşım yok." : "Henüz gönderi yok. İlk fotoğrafı sen paylaş!";

    public bool IsEmpty => Loaded && Items.Count == 0 && !HasError;

    // ---- discovery header: what you are looking at ----
    public string DiscoverTitle => SelectedFamily is { } f ? $"{f} tonları" : "Tüm renkler";
    public string DiscoverSubtitle
    {
        get
        {
            if (SelectedFamily is { } f) { var n = Facets.FirstOrDefault(x => x.Family == f)?.Count ?? 0; return $"{n} paylaşım bu renk ailesinde"; }
            var total = Facets.Sum(x => x.Count);
            return total == 0 ? "Topluluğun paylaştıkları" : $"{Facets.Count} renk ailesinde {total} paylaşım";
        }
    }
    public string DiscoverHex => SelectedFamily is { } f ? Facets.FirstOrDefault(x => x.Family == f)?.Hex ?? "#CCCCCC" : "#CCCCCC";
    public bool HasDiscoverColor => SelectedFamily is not null;
    partial void OnSelectedFamilyChanged(string? value) { OnPropertyChanged(nameof(DiscoverTitle)); OnPropertyChanged(nameof(DiscoverSubtitle)); OnPropertyChanged(nameof(DiscoverHex)); OnPropertyChanged(nameof(HasDiscoverColor)); }
    public bool ShowInitialLoading => IsBusy && Items.Count == 0;

    [RelayCommand]
    private async Task SetScopeAsync(string newScope)
    {
        if (Scope == newScope) return;
        Scope = newScope;
        SelectedFamily = null;
        foreach (var f in Facets) f.IsSelected = false;
        Items.Clear();
        Loaded = false;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task SelectFamilyAsync(FacetItem? facet)
    {
        // tapping the active chip (or "Tümü") clears the filter
        var clear = facet is null || facet.IsSelected;
        SelectedFamily = clear ? null : facet!.Family;
        foreach (var f in Facets) f.IsSelected = !clear && f == facet;
        Items.Clear();
        Loaded = false;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task OpenFriendsAsync() => await Shell.Current.GoToAsync("friends");

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(IsBusy)) OnPropertyChanged(nameof(ShowInitialLoading));
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsRefreshing = true;
        await GuardAsync(async () =>
        {
            var page = await api.GetFeedAsync(null, 10, Scope, SelectedFamily);
            Items.Clear();
            foreach (var p in page.Items) Items.Add(new PostItemViewModel(p, api));
            _cursor = page.NextCursor;
            _hasMore = page.NextCursor is not null;
            Loaded = true;
            await LoadSidebarAsync();
        }, showBusy: Items.Count == 0);
        IsRefreshing = false;
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ShowInitialLoading));
    }

    /// <summary>Colour chips (discovery) and the pending-request badge. Failures here never break the feed.</summary>
    private async Task LoadSidebarAsync()
    {
        try
        {
            var requests = await api.GetFriendRequestsAsync();
            PendingRequests = requests.Incoming.Count;

            if (IsDiscoverTab)
            {
                var facets = await api.GetColorFacetsAsync();
                Facets.Clear();
                foreach (var f in facets) Facets.Add(new FacetItem(f.Family, f.Hex, f.Count) { IsSelected = f.Family == SelectedFamily });
                OnPropertyChanged(nameof(DiscoverSubtitle)); OnPropertyChanged(nameof(DiscoverHex));
            }
        }
        catch (ApiException) { }
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (IsLoadingMore || !_hasMore || IsBusy || IsRefreshing) return;
        IsLoadingMore = true;
        try
        {
            var page = await api.GetFeedAsync(_cursor, 10, Scope, SelectedFamily);
            foreach (var p in page.Items) Items.Add(new PostItemViewModel(p, api));
            _cursor = page.NextCursor;
            _hasMore = page.NextCursor is not null;
        }
        catch (ApiException ex) { InfoMessage = ex.Message; }
        finally { IsLoadingMore = false; }
    }

    [RelayCommand]
    private async Task SharePhotoAsync()
    {
        InfoMessage = null;
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null) return;
        try
        {
            var picked = (await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions { Title = "Paylaşılacak fotoğraf", SelectionLimit = 1 }))?.FirstOrDefault();
            if (picked is null) return;
            var caption = await page.DisplayPromptAsync("Gönderi", "Bir açıklama ekle (isteğe bağlı)", "Paylaş", "Vazgeç", maxLength: 500);
            if (caption is null) return;

            IsBusy = true;
            var post = await publisher.PublishPhotoAsync(picked.FullPath, caption, null);
            if (SelectedFamily is null) Items.Insert(0, new PostItemViewModel(post, api));
            OnPropertyChanged(nameof(IsEmpty));
            InfoMessage = "Gönderin paylaşıldı.";
        }
        catch (ApiException ex) { InfoMessage = ex.Message; }
        catch (PermissionException) { InfoMessage = "Fotoğraflara erişim izni verilmedi."; }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); InfoMessage = "Paylaşım başarısız oldu."; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task OpenPostAsync(PostItemViewModel item)
        => await Shell.Current.GoToAsync("post", new ShellNavigationQueryParameters { ["Item"] = item });

    [RelayCommand]
    private async Task OpenProfileAsync(PostItemViewModel item)
        => await Shell.Current.GoToAsync("profile", new Dictionary<string, object> { ["UserId"] = item.AuthorId });
}

/// <summary>Uploads local images and turns them into posts (used by feed, hunt result and collage).</summary>
public interface IPostPublisher
{
    Task<Post> PublishPhotoAsync(string localJpegPath, string? caption, string? colorHex, CancellationToken ct = default);
    Task<Post> PublishCollageAsync(string localJpegPath, string? caption, string? colorHex, CancellationToken ct = default);
    Task<Post> PublishHuntPhotoAsync(string photoKey, string? caption, string colorHex, CancellationToken ct = default);
}

public sealed class PostPublisher(IApiClient api, ImageService images) : IPostPublisher
{
    public async Task<Post> PublishPhotoAsync(string localPath, string? caption, string? colorHex, CancellationToken ct = default)
    {
        var prepared = await images.PrepareAsync(localPath, ImageService.UploadMaxEdge, ct);
        // tag the post with the photo's dominant colour so it shows up when people browse by colour
        colorHex ??= await Task.Run(() =>
        {
            using var small = ImageService.LoadUpright(prepared.Path, 256);
            return PaletteExtractor.Dominant(small).ToHex();
        }, ct);
        var upload = await api.UploadMediaAsync(prepared.Path, ct);
        return await api.CreatePostAsync(PostType.Photo, caption, colorHex, [upload.Key], ct);
    }

    public async Task<Post> PublishCollageAsync(string localJpegPath, string? caption, string? colorHex, CancellationToken ct = default)
    {
        var upload = await api.UploadMediaAsync(localJpegPath, ct);
        return await api.CreatePostAsync(PostType.Collage, caption, colorHex, [upload.Key], ct);
    }

    public Task<Post> PublishHuntPhotoAsync(string photoKey, string? caption, string colorHex, CancellationToken ct = default)
        => api.CreatePostAsync(PostType.Photo, caption, colorHex, [photoKey], ct);
}

public partial class PostDetailViewModel(IApiClient api, ISessionService session) : BaseViewModel, IQueryAttributable
{
    private string? _cursor;

    [ObservableProperty] private PostItemViewModel? post;
    [ObservableProperty] private string newComment = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(NoComments))] private bool commentsLoaded;
    [ObservableProperty] private bool isSending;
    [ObservableProperty] private bool canLoadMoreComments;
    [ObservableProperty] private bool isOwnPost;

    public ObservableCollection<Comment> Comments { get; } = [];
    public bool NoComments => CommentsLoaded && Comments.Count == 0;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("Item", out var o) && o is PostItemViewModel item)
        {
            Post = item;
            IsOwnPost = session.User?.Id == item.AuthorId;
        }
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (Post is null) return;
        await GuardAsync(async () =>
        {
            var fresh = await api.GetPostAsync(Post.Id);
            Post.Apply(fresh);
            var page = await api.GetCommentsAsync(Post.Id, null);
            Comments.Clear();
            foreach (var c in page.Items) Comments.Add(c);
            _cursor = page.NextCursor;
            CanLoadMoreComments = _cursor is not null;
            CommentsLoaded = true;
            OnPropertyChanged(nameof(NoComments));
        });
        if (Post is { } p) await LoadContextAsync(p);
    }

    // ---- context around the post: who made it, and more of the same colour / the same person ----
    [ObservableProperty, NotifyPropertyChangedFor(nameof(AuthorMeta)), NotifyPropertyChangedFor(nameof(HasAuthorProfile))] private UserProfile? author;
    [ObservableProperty] private string? infoMessage;
    public bool HasAuthorProfile => Author is not null;
    public string AuthorMeta => Author is null ? "" : $"Seviye {Author.Level} · {Author.PostCount} gönderi" + (Author.BestEverScore > 0 ? $" · en iyi {Author.BestEverScore}" : "");
    public ObservableCollection<PostItemViewModel> SameColor { get; } = [];
    public ObservableCollection<PostItemViewModel> MoreByAuthor { get; } = [];
    public bool HasSameColor => SameColor.Count > 0;
    public bool HasMoreByAuthor => MoreByAuthor.Count > 0;
    public string SameColorTitle => Post?.ColorFamily is { } f ? $"Benzer {f.ToLower(new System.Globalization.CultureInfo("tr-TR"))} tonlar" : "Benzer renkler";
    public string MoreByAuthorTitle => Post is null ? "" : $"@{Post.AuthorName} paylaşımları";

    /// <summary>Optional sections, loaded after the post itself; a failure only hides that section.</summary>
    private async Task LoadContextAsync(PostItemViewModel p)
    {
        try { Author = await api.GetUserAsync(p.AuthorId); } catch (ApiException) { Author = null; }

        try
        {
            SameColor.Clear();
            if (p.ColorFamily is { } family)
            {
                var feed = await api.GetFeedAsync(null, 12, "all", family);
                foreach (var x in feed.Items.Where(x => x.Id != p.Id && x.MediaUrls.Count > 0).Take(8)) SameColor.Add(new PostItemViewModel(x, api));
            }
        }
        catch (ApiException) { }

        try
        {
            MoreByAuthor.Clear();
            var mine = await api.GetUserPostsAsync(p.AuthorId, null, 12);
            foreach (var x in mine.Items.Where(x => x.Id != p.Id && x.MediaUrls.Count > 0).Take(8)) MoreByAuthor.Add(new PostItemViewModel(x, api));
        }
        catch (ApiException) { }

        foreach (var n in new[] { nameof(HasSameColor), nameof(HasMoreByAuthor), nameof(SameColorTitle), nameof(MoreByAuthorTitle) }) OnPropertyChanged(n);
    }

    [RelayCommand]
    private async Task OpenOtherAsync(PostItemViewModel item)
        => await Shell.Current.GoToAsync("post", new ShellNavigationQueryParameters { ["Item"] = item });

    [RelayCommand]
    private async Task CopyHexAsync()
    {
        if (Post?.ColorHex is not { } hex) return;
        try { await Clipboard.Default.SetTextAsync(hex); InfoMessage = $"{hex} kopyalandı"; }
        catch { return; }
        await Task.Delay(1500);
        InfoMessage = null;
    }

    [RelayCommand]
    private async Task MoreCommentsAsync()
    {
        if (Post is null || _cursor is null) return;
        await GuardAsync(async () =>
        {
            var page = await api.GetCommentsAsync(Post.Id, _cursor);
            foreach (var c in page.Items) Comments.Add(c);
            _cursor = page.NextCursor;
            CanLoadMoreComments = _cursor is not null;
        }, showBusy: false);
    }

    [RelayCommand]
    private async Task SendCommentAsync()
    {
        var text = NewComment.Trim();
        if (Post is null || text.Length == 0 || IsSending) return;
        try
        {
            IsSending = true;
            ErrorMessage = null;
            var comment = await api.AddCommentAsync(Post.Id, text);
            Comments.Insert(0, comment);
            Post.CommentCount++;
            NewComment = "";
            OnPropertyChanged(nameof(NoComments));
        }
        catch (ApiException ex) { ErrorMessage = ex.Message; }
        finally { IsSending = false; }
    }

    [RelayCommand]
    private async Task BackAsync() => await Shell.Current.GoToAsync("..");

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Post is null) return;
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null || !await page.DisplayAlertAsync("Gönderiyi sil", "Bu gönderi kalıcı olarak silinecek.", "Sil", "Vazgeç")) return;
        await GuardAsync(async () =>
        {
            await api.DeletePostAsync(Post.Id);
            await Shell.Current.GoToAsync("..");
        });
    }

    [RelayCommand]
    private async Task OpenProfileAsync()
    {
        if (Post is null) return;
        await Shell.Current.GoToAsync("profile", new Dictionary<string, object> { ["UserId"] = Post.AuthorId });
    }
}
