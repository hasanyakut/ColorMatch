using System.ComponentModel.DataAnnotations;
using ColorMatch.Domain.Enums;

namespace ColorMatch.Application.DTOs;

// ---- Auth ----
public sealed record RegisterRequest(
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required, RegularExpression(@"^[A-Za-z0-9_.]{3,24}$", ErrorMessage = "User name must be 3-24 characters: letters, digits, underscore or dot.")] string UserName,
    [Required, StringLength(100, MinimumLength = 8)] string Password);

public sealed record LoginRequest(
    [Required, StringLength(256)] string EmailOrUserName,
    [Required, StringLength(100)] string Password);

public sealed record AuthResponse(string Token, DateTime ExpiresAtUtc, UserProfileDto User);

public sealed record AuthUser(Guid Id, string UserName, string Email);
public sealed record AuthToken(string Token, DateTime ExpiresAtUtc);

// ---- Users ----
public sealed record UserProfileDto(
    Guid Id,
    string UserName,
    string? AvatarUrl,
    int TotalXp,
    int Level,
    int LevelStartXp,
    int NextLevelXp,
    int PostCount,
    int BestEverScore,
    int StreakDays,
    DateTime JoinedAtUtc,
    string Title = "");

public sealed record AuthorDto(Guid Id, string UserName, string? AvatarUrl);

// ---- Challenges / hunts ----
public sealed record DailyChallengeDto(
    int Id,
    DateOnly Date,
    string Name,
    string Hex,
    DateTime ResetsAtUtc,
    int MyBestScore,
    int MyXpToday,
    int MyAttemptsToday);

public sealed record HuntAttemptResultDto(
    Guid AttemptId,
    string TargetHex,
    string FoundHex,
    double DeltaE,
    int Score,
    int XpAwarded,
    int DailyBestScore,
    bool IsNewDailyBest,
    int TotalXp,
    int Level,
    string PhotoKey,
    string PhotoUrl,
    RegionDto? Region = null);

/// <summary>Where on the photo the server measured the colour (centre as a fraction of width / height, radius as a fraction of the shorter side). Chosen by the server, never by the client.</summary>
public sealed record RegionDto(double X, double Y, double Radius);

public sealed record HuntAttemptSummaryDto(Guid Id, string FoundHex, int Score, int XpAwarded, string PhotoKey, string PhotoUrl, DateTime CreatedAtUtc);

// ---- Posts ----
public sealed record CreatePostRequest(
    [Required] PostType Type,
    [StringLength(500)] string? Caption,
    [RegularExpression(@"^#[0-9A-Fa-f]{6}$", ErrorMessage = "ColorHex must look like #RRGGBB.")] string? ColorHex,
    [Required, MinLength(1), MaxLength(4)] List<string> MediaKeys);

public sealed record PostDto(
    Guid Id,
    AuthorDto Author,
    PostType Type,
    string? Caption,
    string? ColorHex,
    IReadOnlyList<string> MediaUrls,
    int LikeCount,
    int CommentCount,
    bool LikedByMe,
    DateTime CreatedAtUtc);

public sealed record LikeStateDto(bool Liked, int LikeCount);

public sealed record CreateCommentRequest(
    [Required, StringLength(500, MinimumLength = 1)] string Text);

public sealed record CommentDto(Guid Id, AuthorDto Author, string Text, DateTime CreatedAtUtc);

public sealed record MediaUploadResponse(string Key, string Url);

// ---- Home extras ----
public sealed record LeaderboardEntryDto(int Rank, Guid UserId, string UserName, string? AvatarUrl, int BestScore, bool IsMe);

public sealed record LeaderboardDto(IReadOnlyList<LeaderboardEntryDto> Entries, int? MyRank, int TotalPlayers);

public sealed record PastChallengeDto(DateOnly Date, string Name, string Hex, int MyBestScore);
