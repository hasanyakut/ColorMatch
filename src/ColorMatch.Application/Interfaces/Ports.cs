using ColorMatch.Application.Common;
using ColorMatch.Application.DTOs;
using ColorMatch.ColorEngine.ColorConversion;
using ColorMatch.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ColorMatch.Application.Interfaces;

/// <summary>Application-facing view of the persistence model (implemented by EF Core in Infrastructure).</summary>
public interface IAppDbContext
{
    DbSet<UserProfile> Profiles { get; }
    DbSet<UserProgress> Progress { get; }
    DbSet<DailyColorChallenge> Challenges { get; }
    DbSet<ColorHuntAttempt> HuntAttempts { get; }
    DbSet<XpTransaction> XpTransactions { get; }
    DbSet<DailyHunt> DailyHunts { get; }
    DbSet<DailyHuntAttempt> DailyHuntAttempts { get; }
    DbSet<UserAtlasColor> UserAtlasColors { get; }
    DbSet<BingoBoard> BingoBoards { get; }
    DbSet<BingoCell> BingoCells { get; }
    DbSet<BingoAttempt> BingoAttempts { get; }
    DbSet<Duel> Duels { get; }
    DbSet<DuelAttempt> DuelAttempts { get; }
    Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker ChangeTracker { get; }
    DbSet<Post> Posts { get; }
    DbSet<PostMedia> PostMedia { get; }
    DbSet<PostLike> PostLikes { get; }
    DbSet<PostComment> PostComments { get; }
    DbSet<Friendship> Friendships { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IIdentityService
{
    /// <summary>Creates the credential record. Throws ValidationAppException on duplicate / weak password.</summary>
    Task<AuthUser> CreateUserAsync(string email, string userName, string password, CancellationToken ct);
    /// <summary>Returns null when credentials are wrong.</summary>
    Task<AuthUser?> ValidateCredentialsAsync(string emailOrUserName, string password, CancellationToken ct);
    Task DeleteUserAsync(Guid userId, CancellationToken ct);
}

public interface ITokenService
{
    AuthToken CreateToken(AuthUser user);
}

public interface IFileStorage
{
    Task SaveAsync(string key, Stream content, CancellationToken ct);
    Task<bool> ExistsAsync(string key, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
}

public sealed record NormalizedImage(byte[] JpegBytes, int Width, int Height);

/// <summary>A small copy of a photo as a pixel grid (row-major), used to search it for the best matching region.</summary>
public sealed record PixelGrid(int Width, int Height, Rgb[] Pixels);

public interface IImageProcessor
{
    /// <summary>
    /// Decodes (and thereby validates) an image, applies EXIF orientation, strips metadata,
    /// downsizes to a maximum edge and re-encodes as JPEG. Throws ValidationAppException for non-images.
    /// </summary>
    NormalizedImage Normalize(Stream input, int maxEdge);

    /// <summary>Returns the pixels inside a circle (centre normalised 0..1, radius = fraction of the shorter side).</summary>
    IReadOnlyList<Rgb> SampleCircle(byte[] jpegBytes, double centerX, double centerY, double radius);

    /// <summary>Scales the photo down so that its longer side is at most <paramref name="maxEdge"/> pixels.</summary>
    PixelGrid Downscale(byte[] jpegBytes, int maxEdge);
}

// ---- Use-case services ----
public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct);
}

public interface IUserService
{
    Task<UserProfileDto> GetProfileAsync(Guid userId, CancellationToken ct);
    Task<UserProfileDto> SetAvatarAsync(Guid userId, Stream image, CancellationToken ct);
}

public interface IChallengeService
{
    Task<DailyChallengeDto> GetTodayAsync(Guid? userId, CancellationToken ct);
    Task<DailyColorChallenge> GetOrCreateTodayEntityAsync(CancellationToken ct);
    DateOnly TodayLocal();
    /// <summary>The instant the given game day ends (next local midnight of the game time zone).</summary>
    DateTime NextResetUtc(DateOnly date);
    Task<LeaderboardDto> GetLeaderboardAsync(Guid userId, int take, CancellationToken ct);
    Task<IReadOnlyList<PastChallengeDto>> GetRecentAsync(Guid userId, int days, CancellationToken ct);
}

public sealed record HuntSubmission(Guid UserId, Stream Photo);

public interface IColorHuntService
{
    Task<HuntAttemptResultDto> SubmitAsync(HuntSubmission submission, CancellationToken ct);
    Task<IReadOnlyList<HuntAttemptSummaryDto>> GetMyTodayAsync(Guid userId, CancellationToken ct);
}

public interface IPostService
{
    Task<PostDto> CreateAsync(Guid userId, CreatePostRequest request, CancellationToken ct);
    Task<PostDto> GetAsync(Guid? viewerId, Guid postId, CancellationToken ct);
    Task<PagedResult<PostDto>> GetFeedAsync(Guid? viewerId, Guid? authorId, FeedScope scope, string? colorFamily, string? cursor, int pageSize, CancellationToken ct);
    Task<IReadOnlyList<ColorFacetDto>> GetColorFacetsAsync(CancellationToken ct);
    Task<LikeStateDto> SetLikeAsync(Guid userId, Guid postId, bool liked, CancellationToken ct);
    Task DeleteAsync(Guid userId, Guid postId, CancellationToken ct);
}

public interface ICommentService
{
    Task<CommentDto> AddAsync(Guid userId, Guid postId, CreateCommentRequest request, CancellationToken ct);
    Task<PagedResult<CommentDto>> ListAsync(Guid postId, string? cursor, int pageSize, CancellationToken ct);
}

public interface IMediaService
{
    Task<MediaUploadResponse> UploadAsync(Guid userId, Stream content, CancellationToken ct);
}

public sealed record DailyHuntSubmission(Guid UserId, int HuntId, Stream Photo);

public interface IDailyHuntService
{
    /// <summary>Today's three hunts (created on first use) with the caller's best scores and completion state.</summary>
    Task<DailyHuntsDto> GetTodayAsync(Guid userId, CancellationToken ct);
    Task<DailyProgressDto> GetProgressAsync(Guid userId, CancellationToken ct);
    /// <summary>The caller's last seven game days: completed hunts per day (a day counts when at least one hunt was completed).</summary>
    Task<WeekDto> GetWeekAsync(Guid userId, CancellationToken ct);
    Task<DailyHuntAttemptResultDto> SubmitAttemptAsync(DailyHuntSubmission submission, CancellationToken ct);
    /// <summary>Today's ranking: the sum of each player's best score across the three hunts.</summary>
    Task<LeaderboardDto> GetLeaderboardAsync(Guid userId, int take, CancellationToken ct);
}

public sealed record AtlasUnlockStaged(AtlasUnlockDto Unlock, ColorMatch.Application.Services.RewardResult? CompletionReward);

public interface IAtlasService
{
    /// <summary>The caller's atlas: every collection with locked / unlocked colours.</summary>
    Task<AtlasDto> GetAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Called by the hunt service for a VALID attempt: stages the unlock of the hunt's colour (and the collection reward when it completes
    /// a collection) in the current unit of work. Does not save. Returns null when the target is not an atlas colour or is already unlocked.
    /// </summary>
    Task<AtlasUnlockStaged?> StageUnlockAsync(Guid userId, string targetHex, Guid attemptId, int score, CancellationToken ct);
}

public sealed record DuelSubmission(Guid UserId, Guid DuelId, Stream Photo);

public interface IDuelService
{
    /// <summary>The caller's duels (expired ones are settled first). Only accepted friends can be challenged.</summary>
    Task<DuelListDto> ListAsync(Guid userId, CancellationToken ct);
    Task<DuelDto> GetAsync(Guid userId, Guid duelId, CancellationToken ct);
    Task<DuelDto> CreateAsync(Guid userId, Guid opponentId, CancellationToken ct);
    Task<DuelDto> AcceptAsync(Guid userId, Guid duelId, CancellationToken ct);
    /// <summary>Refuses an invitation (opponent) or withdraws it (challenger). Only while Pending.</summary>
    Task<DuelDto> DeclineAsync(Guid userId, Guid duelId, CancellationToken ct);
    Task<DuelAttemptResultDto> SubmitAttemptAsync(DuelSubmission submission, CancellationToken ct);
    /// <summary>The caller stops playing (keeps the best score so far).</summary>
    Task<DuelDto> FinishAsync(Guid userId, Guid duelId, CancellationToken ct);
}

public sealed record BingoSubmission(Guid UserId, int CellIndex, Stream Photo);

public interface IBingoService
{
    /// <summary>The caller's active board (a new one is generated on first use, or after the previous board was completed).</summary>
    Task<BingoBoardDto> GetCurrentAsync(Guid userId, CancellationToken ct);

    /// <summary>One photo for one square of the active board. Score, completion, line rewards and the board bonus are decided on the server.</summary>
    Task<BingoAttemptResultDto> SubmitAttemptAsync(BingoSubmission submission, CancellationToken ct);
}
