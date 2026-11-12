namespace ColorMatch.Mobile.Models;

// Client-side mirrors of the API DTOs (kept separate from view models on purpose).

public enum PostType { Photo = 0, Collage = 1 }

public sealed record UserProfile(
    Guid Id, string UserName, string? AvatarUrl, int TotalXp, int Level,
    int LevelStartXp, int NextLevelXp, int PostCount, int BestEverScore, int StreakDays, DateTime JoinedAtUtc, string? Title = null);

public sealed record AuthResponse(string Token, DateTime ExpiresAtUtc, UserProfile User);

public sealed record Author(Guid Id, string UserName, string? AvatarUrl);

// ---- Daily Hunt (three server-chosen colours per game day) ----
public sealed record DailyHunt(
    int Id, DateOnly Date, string Difficulty, string Name, string Hex, int BaseXp, int MinScore,
    int BestScore, string? BestTier, bool Completed, int Attempts, int XpEarned, string? BestPhotoUrl);

public sealed record DailyProgress(
    int Completed, int Total, bool AllCompleted, int CompletionBonusXp, bool BonusEarned, int XpEarnedToday, int XpAvailableToday);

public sealed record DailyHuntsState(DateOnly Date, DateTime ResetsAtUtc, List<DailyHunt> Hunts, DailyProgress Progress);

public sealed record DailyHuntResult(
    Guid AttemptId, int HuntId, string Difficulty, string TargetHex, string FoundHex, double DeltaE, int Score, string Tier, bool Passed, int MinScore,
    int XpAwarded, bool FirstCompletion, int BestScore, bool IsNewBest, int BonusXpAwarded, int CompletedCount, bool AllCompleted,
    int TotalXp, int Level, bool LeveledUp, string Title, bool TitleChanged, int CurrentStreak, bool StreakExtended, string PhotoKey, string PhotoUrl, AtlasUnlock? AtlasUnlock = null, BingoResult? Bingo = null, DuelAttemptResult? Duel = null, RegionInfo? Region = null);

/// <summary>Where the server measured the colour on the photo (it chooses the spot itself): centre as a fraction of width / height, radius as a fraction of the shorter side, share = part of the whole picture that has this colour (the score is weighted by it).</summary>
public sealed record RegionInfo(double X, double Y, double Radius, double Share = 1);

// ---- the last seven game days (week strip on the home screen) ----
public sealed record WeekDay(DateOnly Date, int Completed, int Total, bool IsToday);

public sealed record WeekData(List<WeekDay> Days, int ActiveDays, int PerfectDays);

// ---- Color Bingo ----
public sealed record BingoCell(int Index, string ColorKey, string Name, string Hex, string Difficulty, bool Completed, int BestScore, int Attempts, int AttemptsLeft);

public sealed record BingoLine(string Id, string Name, List<int> Cells, bool Completed, int Xp);

public sealed record BingoBoard(
    Guid BoardId, string Status, DateTime CreatedAtUtc, List<BingoCell> Cells, List<BingoLine> Lines, int CompletedCells, int CompletedLines,
    bool BoardCompleted, int LineXp, int BoardXp, int MinScore, int MaxAttemptsPerCell, int BoardsCompleted);

public sealed record BingoLineReward(string Id, string Name, int Xp);

/// <summary>Outcome of one attempt on a bingo square, computed by the server.</summary>
public sealed record BingoResult(
    Guid AttemptId, int CellIndex, string ColorName, string TargetHex, string FoundHex, double DeltaE, int Score, string Tier, bool Passed, int MinScore,
    bool CellCompleted, int BestScore, bool IsNewBest, int CompletedCells, int CompletedLines, List<BingoLineReward> NewLines, int LineXpAwarded,
    bool BoardCompleted, int BoardXpAwarded, int TotalXp, int Level, bool LeveledUp, string Title, bool TitleChanged, AtlasUnlock? AtlasUnlock = null, RegionInfo? Region = null);

// ---- Color Atlas ----
public sealed record AtlasColor(string Key, string? Name, string? Hex, bool Unlocked, DateTime? UnlockedAtUtc, int? BestScore, bool IsTodaysHunt, string Difficulty);

public sealed record AtlasCollection(
    string Key, string Name, string Description, int Unlocked, int Total, bool Completed, bool CompletionXpEarned, int CompletionXp, List<AtlasColor> Colors);

public sealed record AtlasData(int Unlocked, int Total, List<AtlasCollection> Collections);

/// <summary>A colour a valid hunt attempt just added to the atlas.</summary>
public sealed record AtlasUnlock(
    string ColorKey, string Name, string Hex, string CollectionKey, string CollectionName, int CollectionUnlocked, int CollectionTotal,
    bool CollectionCompleted, int CompletionXpAwarded);

public sealed record Post(
    Guid Id, Author Author, PostType Type, string? Caption, string? ColorHex,
    List<string> MediaUrls, int LikeCount, int CommentCount, bool LikedByMe, DateTime CreatedAtUtc);

public sealed record Comment(Guid Id, Author Author, string Text, DateTime CreatedAtUtc);

public sealed record Paged<T>(List<T> Items, string? NextCursor);

public sealed record LikeState(bool Liked, int LikeCount);

public sealed record MediaUpload(string Key, string Url);

public sealed record RegisterBody(string Email, string UserName, string Password);
public sealed record LoginBody(string EmailOrUserName, string Password);
public sealed record CreatePostBody(PostType Type, string? Caption, string? ColorHex, List<string> MediaKeys);
public sealed record CreateCommentBody(string Text);

public sealed record LeaderboardEntry(int Rank, Guid UserId, string UserName, string? AvatarUrl, int BestScore, bool IsMe);
public sealed record Leaderboard(List<LeaderboardEntry> Entries, int? MyRank, int TotalPlayers);
public sealed record PastChallenge(DateOnly Date, string Name, string Hex, int MyBestScore);

public sealed record Friend(Guid UserId, string UserName, string? AvatarUrl, int Level, DateTime? SinceUtc);
public sealed record FriendRequests(List<Friend> Incoming, List<Friend> Outgoing);
public sealed record UserSearchResult(Guid UserId, string UserName, string? AvatarUrl, int Level, string Relationship);
public sealed record RelationshipResult(string Relationship);
public sealed record SendFriendRequestBody(Guid UserId);
public sealed record ColorFacet(string Family, string Hex, int Count);
