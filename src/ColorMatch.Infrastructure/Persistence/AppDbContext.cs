using ColorMatch.Application.Interfaces;
using ColorMatch.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ColorMatch.Infrastructure.Persistence;

/// <summary>Credential record (ASP.NET Core Identity). Its key is shared with <see cref="UserProfile"/>.</summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IAppDbContext
{
    public DbSet<UserProfile> Profiles => Set<UserProfile>();
    public DbSet<UserProgress> Progress => Set<UserProgress>();
    public DbSet<DailyColorChallenge> Challenges => Set<DailyColorChallenge>();
    public DbSet<ColorHuntAttempt> HuntAttempts => Set<ColorHuntAttempt>();
    public DbSet<XpTransaction> XpTransactions => Set<XpTransaction>();
    public DbSet<DailyHunt> DailyHunts => Set<DailyHunt>();
    public DbSet<DailyHuntAttempt> DailyHuntAttempts => Set<DailyHuntAttempt>();
    public DbSet<UserAtlasColor> UserAtlasColors => Set<UserAtlasColor>();
    public DbSet<BingoBoard> BingoBoards => Set<BingoBoard>();
    public DbSet<BingoCell> BingoCells => Set<BingoCell>();
    public DbSet<BingoAttempt> BingoAttempts => Set<BingoAttempt>();
    public DbSet<Duel> Duels => Set<Duel>();
    public DbSet<DuelAttempt> DuelAttempts => Set<DuelAttempt>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<PostMedia> PostMedia => Set<PostMedia>();
    public DbSet<PostLike> PostLikes => Set<PostLike>();
    public DbSet<PostComment> PostComments => Set<PostComment>();
    public DbSet<Friendship> Friendships => Set<Friendship>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
