namespace ColorMatch.Domain.Entities;

/// <summary>Public profile. Shares its key with the identity user (ApplicationUser) in Infrastructure.</summary>
public class UserProfile
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string? AvatarKey { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public UserProgress? Progress { get; set; }
}
