namespace ColorMatch.Domain.Entities;

/// <summary>A colour a player has unlocked in the Color Atlas. Unique per (UserId, ColorKey): a colour is unlocked once.</summary>
public class UserAtlasColor
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    /// <summary>Key of the colour in <c>AtlasCatalog</c> (for example "forest-03").</summary>
    public string ColorKey { get; set; } = string.Empty;
    public string CollectionKey { get; set; } = string.Empty;
    /// <summary>The valid attempt that unlocked it (kept as proof of the unlock; no foreign key so history survives clean-ups).</summary>
    public Guid AttemptId { get; set; }
    public int Score { get; set; }
    public DateTime UnlockedAtUtc { get; set; }
}
