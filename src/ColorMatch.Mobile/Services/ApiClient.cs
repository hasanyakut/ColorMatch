using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ColorMatch.Mobile.Models;

namespace ColorMatch.Mobile.Services;

/// <summary>Expected failure with a user-presentable message.</summary>
public sealed class ApiException(string message, HttpStatusCode? status = null, IReadOnlyDictionary<string, string[]>? errors = null)
    : Exception(message)
{
    public HttpStatusCode? Status { get; } = status;
    public IReadOnlyDictionary<string, string[]>? Errors { get; } = errors;
    public bool IsNetwork => Status is null;
}

public interface IApiClient
{
    Task<AuthResponse> RegisterAsync(string email, string userName, string password, CancellationToken ct = default);
    Task<AuthResponse> LoginAsync(string emailOrUserName, string password, CancellationToken ct = default);
    Task<UserProfile> GetMeAsync(CancellationToken ct = default);
    Task<UserProfile> GetUserAsync(Guid id, CancellationToken ct = default);
    Task<UserProfile> UploadAvatarAsync(string filePath, CancellationToken ct = default);
    Task<Paged<Post>> GetUserPostsAsync(Guid id, string? cursor, int pageSize = 24, CancellationToken ct = default);

    /// <summary>Today's three hunts (easy / medium / hard) with the caller's state.</summary>
    Task<DailyHuntsState> GetDailyHuntsAsync(CancellationToken ct = default);
    /// <summary>Submits a photo for one hunt. Score, XP, streak and bonus are decided by the server.</summary>
    Task<DailyHuntResult> SubmitDailyHuntAttemptAsync(int huntId, string photoPath, double x, double y, double radius, CancellationToken ct = default);
    Task<Leaderboard> GetLeaderboardAsync(int take = 5, CancellationToken ct = default);
    /// <summary>The player's last seven game days: completed hunts per day.</summary>
    Task<WeekData> GetWeekAsync(CancellationToken ct = default);
    /// <summary>The player's Color Atlas: five collections, locked and unlocked colours.</summary>
    Task<AtlasData> GetAtlasAsync(CancellationToken ct = default);
    /// <summary>The player's active 3×3 bingo board (a new one after a completed board).</summary>
    Task<BingoBoard> GetBingoAsync(CancellationToken ct = default);
    /// <summary>Submits a photo for one bingo square. Score, completion, line rewards and the board bonus are decided by the server.</summary>
    Task<BingoResult> SubmitBingoAttemptAsync(int cellIndex, string photoPath, double x, double y, double radius, CancellationToken ct = default);
    /// <summary>Friend duels: list, challenge a friend, answer an invitation, play (3 attempts) or stop. Scores, winner and XP are decided by the server.</summary>
    Task<DuelList> GetDuelsAsync(CancellationToken ct = default);
    Task<Duel> CreateDuelAsync(Guid opponentId, CancellationToken ct = default);
    Task<Duel> AcceptDuelAsync(Guid id, CancellationToken ct = default);
    Task<Duel> DeclineDuelAsync(Guid id, CancellationToken ct = default);
    Task<Duel> FinishDuelAsync(Guid id, CancellationToken ct = default);
    Task<DuelAttemptResult> SubmitDuelAttemptAsync(Guid id, string photoPath, double x, double y, double radius, CancellationToken ct = default);
    Task<List<PastChallenge>> GetRecentAsync(int days = 6, CancellationToken ct = default);

    Task<MediaUpload> UploadMediaAsync(string filePath, CancellationToken ct = default);
    Task<Post> CreatePostAsync(PostType type, string? caption, string? colorHex, IEnumerable<string> mediaKeys, CancellationToken ct = default);
    Task<Paged<Post>> GetFeedAsync(string? cursor, int pageSize = 10, string scope = "all", string? color = null, CancellationToken ct = default);
    Task<List<ColorFacet>> GetColorFacetsAsync(CancellationToken ct = default);

    Task<List<Friend>> GetFriendsAsync(CancellationToken ct = default);
    Task<FriendRequests> GetFriendRequestsAsync(CancellationToken ct = default);
    Task<List<UserSearchResult>> SearchUsersAsync(string query, CancellationToken ct = default);
    Task<RelationshipResult> GetRelationshipAsync(Guid userId, CancellationToken ct = default);
    Task<RelationshipResult> SendFriendRequestAsync(Guid userId, CancellationToken ct = default);
    Task<RelationshipResult> AcceptFriendRequestAsync(Guid userId, CancellationToken ct = default);
    Task<RelationshipResult> RemoveFriendAsync(Guid userId, CancellationToken ct = default);
    Task<Post> GetPostAsync(Guid id, CancellationToken ct = default);
    Task<LikeState> SetLikeAsync(Guid postId, bool liked, CancellationToken ct = default);
    Task<Paged<Comment>> GetCommentsAsync(Guid postId, string? cursor, CancellationToken ct = default);
    Task<Comment> AddCommentAsync(Guid postId, string text, CancellationToken ct = default);
    Task DeletePostAsync(Guid id, CancellationToken ct = default);

    /// <summary>Turns a server-relative media URL into an absolute URL for image controls.</summary>
    string? ToAbsoluteUrl(string? relative);
}

/// <summary>
/// Central HTTP layer: base address, bearer token, timeouts, cancellation and problem-details parsing.
/// </summary>
public sealed class ApiClient(HttpClient http, AppSettings settings, ISessionService session) : IApiClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan UploadTimeout = TimeSpan.FromSeconds(90);

    public string? ToAbsoluteUrl(string? relative)
    {
        if (string.IsNullOrEmpty(relative)) return null;
        return relative.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? relative : settings.BaseUrl.TrimEnd('/') + relative;
    }

    // ---- auth / users ----
    public Task<AuthResponse> RegisterAsync(string email, string userName, string password, CancellationToken ct = default)
        => SendAsync<AuthResponse>(HttpMethod.Post, "api/auth/register", new RegisterBody(email, userName, password), ct, auth: false);

    public Task<AuthResponse> LoginAsync(string emailOrUserName, string password, CancellationToken ct = default)
        => SendAsync<AuthResponse>(HttpMethod.Post, "api/auth/login", new LoginBody(emailOrUserName, password), ct, auth: false);

    public Task<UserProfile> GetMeAsync(CancellationToken ct = default) => SendAsync<UserProfile>(HttpMethod.Get, "api/users/me", null, ct);
    public Task<UserProfile> GetUserAsync(Guid id, CancellationToken ct = default) => SendAsync<UserProfile>(HttpMethod.Get, $"api/users/{id}", null, ct);

    public Task<UserProfile> UploadAvatarAsync(string filePath, CancellationToken ct = default)
        => UploadFileAsync<UserProfile>("api/users/me/avatar", filePath, ct);

    public Task<Paged<Post>> GetUserPostsAsync(Guid id, string? cursor, int pageSize = 24, CancellationToken ct = default)
        => SendAsync<Paged<Post>>(HttpMethod.Get, $"api/users/{id}/posts?pageSize={pageSize}{CursorQuery(cursor)}", null, ct);

    // ---- game ----
    public Task<DailyHuntsState> GetDailyHuntsAsync(CancellationToken ct = default)
        => SendAsync<DailyHuntsState>(HttpMethod.Get, "api/game/daily-hunts", null, ct);

    public Task<AtlasData> GetAtlasAsync(CancellationToken ct = default)
        => SendAsync<AtlasData>(HttpMethod.Get, "api/game/atlas", null, ct);

    public Task<BingoBoard> GetBingoAsync(CancellationToken ct = default)
        => SendAsync<BingoBoard>(HttpMethod.Get, "api/game/bingo", null, ct);

    public async Task<BingoResult> SubmitBingoAttemptAsync(int cellIndex, string photoPath, double x, double y, double radius, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(photoPath);
        using var form = new MultipartFormDataContent();
        var file = new StreamContent(stream);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "Photo", "photo.jpg");
        form.Add(new StringContent(Inv(x)), "RegionX");
        form.Add(new StringContent(Inv(y)), "RegionY");
        form.Add(new StringContent(Inv(radius)), "RegionRadius");
        return await SendAsync<BingoResult>(HttpMethod.Post, $"api/game/bingo/cells/{cellIndex}/attempts", form, ct, timeout: UploadTimeout);
    }

    public Task<DuelList> GetDuelsAsync(CancellationToken ct = default) => SendAsync<DuelList>(HttpMethod.Get, "api/game/duels", null, ct);
    public Task<Duel> CreateDuelAsync(Guid opponentId, CancellationToken ct = default) => SendAsync<Duel>(HttpMethod.Post, "api/game/duels", new CreateDuelBody(opponentId), ct);
    public Task<Duel> AcceptDuelAsync(Guid id, CancellationToken ct = default) => SendAsync<Duel>(HttpMethod.Post, $"api/game/duels/{id}/accept", null, ct);
    public Task<Duel> DeclineDuelAsync(Guid id, CancellationToken ct = default) => SendAsync<Duel>(HttpMethod.Post, $"api/game/duels/{id}/decline", null, ct);
    public Task<Duel> FinishDuelAsync(Guid id, CancellationToken ct = default) => SendAsync<Duel>(HttpMethod.Post, $"api/game/duels/{id}/finish", null, ct);

    public async Task<DuelAttemptResult> SubmitDuelAttemptAsync(Guid id, string photoPath, double x, double y, double radius, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(photoPath);
        using var form = new MultipartFormDataContent();
        var file = new StreamContent(stream);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "Photo", "photo.jpg");
        form.Add(new StringContent(Inv(x)), "RegionX");
        form.Add(new StringContent(Inv(y)), "RegionY");
        form.Add(new StringContent(Inv(radius)), "RegionRadius");
        return await SendAsync<DuelAttemptResult>(HttpMethod.Post, $"api/game/duels/{id}/attempts", form, ct, timeout: UploadTimeout);
    }

    public Task<WeekData> GetWeekAsync(CancellationToken ct = default)
        => SendAsync<WeekData>(HttpMethod.Get, "api/game/daily-hunts/week", null, ct);

    public Task<Leaderboard> GetLeaderboardAsync(int take = 5, CancellationToken ct = default)
        => SendAsync<Leaderboard>(HttpMethod.Get, $"api/game/daily-hunts/leaderboard?take={take}", null, ct);

    public Task<List<PastChallenge>> GetRecentAsync(int days = 6, CancellationToken ct = default)
        => SendAsync<List<PastChallenge>>(HttpMethod.Get, $"api/challenges/recent?days={days}", null, ct);

    public async Task<DailyHuntResult> SubmitDailyHuntAttemptAsync(int huntId, string photoPath, double x, double y, double radius, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(photoPath);
        using var form = new MultipartFormDataContent();
        var file = new StreamContent(stream);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "Photo", "photo.jpg");
        form.Add(new StringContent(Inv(x)), "RegionX");
        form.Add(new StringContent(Inv(y)), "RegionY");
        form.Add(new StringContent(Inv(radius)), "RegionRadius");
        return await SendAsync<DailyHuntResult>(HttpMethod.Post, $"api/game/daily-hunts/{huntId}/attempts", form, ct, timeout: UploadTimeout);
    }

    // ---- social ----
    public Task<MediaUpload> UploadMediaAsync(string filePath, CancellationToken ct = default)
        => UploadFileAsync<MediaUpload>("api/media", filePath, ct);

    public Task<Post> CreatePostAsync(PostType type, string? caption, string? colorHex, IEnumerable<string> mediaKeys, CancellationToken ct = default)
        => SendAsync<Post>(HttpMethod.Post, "api/posts", new CreatePostBody(type, caption, colorHex, mediaKeys.ToList()), ct);

    public Task<Paged<Post>> GetFeedAsync(string? cursor, int pageSize = 10, string scope = "all", string? color = null, CancellationToken ct = default)
        => SendAsync<Paged<Post>>(HttpMethod.Get,
            $"api/posts?scope={scope}&pageSize={pageSize}{(string.IsNullOrEmpty(color) ? "" : "&color=" + Uri.EscapeDataString(color))}{CursorQuery(cursor)}", null, ct);

    public Task<List<ColorFacet>> GetColorFacetsAsync(CancellationToken ct = default)
        => SendAsync<List<ColorFacet>>(HttpMethod.Get, "api/posts/colors", null, ct);

    // ---- friends ----
    public Task<List<Friend>> GetFriendsAsync(CancellationToken ct = default) => SendAsync<List<Friend>>(HttpMethod.Get, "api/friends", null, ct);
    public Task<FriendRequests> GetFriendRequestsAsync(CancellationToken ct = default) => SendAsync<FriendRequests>(HttpMethod.Get, "api/friends/requests", null, ct);
    public Task<List<UserSearchResult>> SearchUsersAsync(string query, CancellationToken ct = default)
        => SendAsync<List<UserSearchResult>>(HttpMethod.Get, $"api/friends/search?q={Uri.EscapeDataString(query)}", null, ct);
    public Task<RelationshipResult> GetRelationshipAsync(Guid userId, CancellationToken ct = default) => SendAsync<RelationshipResult>(HttpMethod.Get, $"api/friends/status/{userId}", null, ct);
    public Task<RelationshipResult> SendFriendRequestAsync(Guid userId, CancellationToken ct = default)
        => SendAsync<RelationshipResult>(HttpMethod.Post, "api/friends/requests", new SendFriendRequestBody(userId), ct);
    public Task<RelationshipResult> AcceptFriendRequestAsync(Guid userId, CancellationToken ct = default)
        => SendAsync<RelationshipResult>(HttpMethod.Post, $"api/friends/requests/{userId}/accept", null, ct);
    public Task<RelationshipResult> RemoveFriendAsync(Guid userId, CancellationToken ct = default)
        => SendAsync<RelationshipResult>(HttpMethod.Delete, $"api/friends/{userId}", null, ct);

    public Task<Post> GetPostAsync(Guid id, CancellationToken ct = default) => SendAsync<Post>(HttpMethod.Get, $"api/posts/{id}", null, ct);

    public Task<LikeState> SetLikeAsync(Guid postId, bool liked, CancellationToken ct = default)
        => SendAsync<LikeState>(liked ? HttpMethod.Put : HttpMethod.Delete, $"api/posts/{postId}/like", null, ct);

    public Task<Paged<Comment>> GetCommentsAsync(Guid postId, string? cursor, CancellationToken ct = default)
        => SendAsync<Paged<Comment>>(HttpMethod.Get, $"api/posts/{postId}/comments?pageSize=30{CursorQuery(cursor)}", null, ct);

    public Task<Comment> AddCommentAsync(Guid postId, string text, CancellationToken ct = default)
        => SendAsync<Comment>(HttpMethod.Post, $"api/posts/{postId}/comments", new CreateCommentBody(text), ct);

    public Task DeletePostAsync(Guid id, CancellationToken ct = default)
        => SendAsync<object?>(HttpMethod.Delete, $"api/posts/{id}", null, ct);

    // ---- plumbing ----
    private async Task<T> UploadFileAsync<T>(string path, string filePath, CancellationToken ct)
    {
        await using var stream = File.OpenRead(filePath);
        using var form = new MultipartFormDataContent();
        var file = new StreamContent(stream);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "file", Path.GetFileName(filePath));
        return await SendAsync<T>(HttpMethod.Post, path, form, ct, timeout: UploadTimeout);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct, bool auth = true, TimeSpan? timeout = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(new Uri(settings.BaseUrl.TrimEnd('/') + "/"), path));
        if (auth && session.Token is { } token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is HttpContent content) request.Content = content;
        else if (body is not null) request.Content = JsonContent.Create(body, options: Json);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? DefaultTimeout);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        {
            throw new ApiException("İstek zaman aşımına uğradı. Bağlantını kontrol edip tekrar dene.");
        }
        catch (HttpRequestException)
        {
            throw new ApiException($"Sunucuya ulaşılamadı ({settings.BaseUrl}). Sunucunun çalıştığından ve adresin doğru olduğundan emin ol.");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.NoContent) return default!;
                try
                {
                    return (await response.Content.ReadFromJsonAsync<T>(Json, cts.Token))
                           ?? throw new ApiException("Sunucudan beklenmeyen boş yanıt alındı.", response.StatusCode);
                }
                catch (JsonException) { throw new ApiException("Sunucu yanıtı okunamadı.", response.StatusCode); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                { throw new ApiException("İstek zaman aşımına uğradı."); }
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized && auth)
                session.NotifyExpired();

            throw await ReadProblemAsync(response, cts.Token);
        }
    }

    private static async Task<ApiException> ReadProblemAsync(HttpResponseMessage response, CancellationToken ct)
    {
        string? detail = null;
        Dictionary<string, string[]>? errors = null;
        try
        {
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;
            if (root.TryGetProperty("errors", out var errs) && errs.ValueKind == JsonValueKind.Object)
            {
                errors = [];
                foreach (var p in errs.EnumerateObject())
                    errors[p.Name] = p.Value.EnumerateArray().Select(e => e.GetString() ?? "").ToArray();
            }
            if (root.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.String) detail = d.GetString();
        }
        catch { /* not JSON */ }

        var message = errors is { Count: > 0 }
            ? string.Join("\n", errors.Values.SelectMany(v => v))
            : detail;

        message ??= response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Oturum geçersiz. Lütfen tekrar giriş yap.",
            HttpStatusCode.TooManyRequests => "Çok fazla deneme yaptın. Biraz bekleyip tekrar dene.",
            HttpStatusCode.RequestEntityTooLarge => "Dosya çok büyük.",
            _ => $"Sunucu hatası ({(int)response.StatusCode})."
        };
        if (response.StatusCode == HttpStatusCode.Unauthorized && detail == "Invalid credentials.")
            message = "E-posta/kullanıcı adı veya şifre hatalı.";
        return new ApiException(message, response.StatusCode, errors);
    }

    private static string CursorQuery(string? cursor) => string.IsNullOrEmpty(cursor) ? "" : "&cursor=" + Uri.EscapeDataString(cursor);
    private static string Inv(double v) => v.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture);
}
