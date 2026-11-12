using ColorMatch.Domain.Enums;

namespace ColorMatch.Domain.Entities;

public class Post
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public UserProfile? User { get; set; }
    public PostType Type { get; set; }
    public string? Caption { get; set; }
    /// <summary>Optional colour the post is themed around (e.g. the hunt target).</summary>
    public string? ColorHex { get; set; }
    /// <summary>Colour family derived from <see cref="ColorHex"/> (e.g. "Mavi"); used for browsing by colour.</summary>
    public string? ColorFamily { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public List<PostMedia> Media { get; set; } = [];
    public List<PostLike> Likes { get; set; } = [];
    public List<PostComment> Comments { get; set; } = [];
}

public class PostMedia
{
    public Guid Id { get; set; }
    public Guid PostId { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public class PostLike
{
    public Guid PostId { get; set; }
    public Guid UserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class PostComment
{
    public Guid Id { get; set; }
    public Guid PostId { get; set; }
    public Guid UserId { get; set; }
    public UserProfile? User { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
