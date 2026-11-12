using ColorMatch.Domain.Entities;
using ColorMatch.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ColorMatch.Infrastructure.Configurations;

internal sealed class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> b)
    {
        b.ToTable("UserProfiles");
        b.HasKey(x => x.Id);
        b.Property(x => x.UserName).HasMaxLength(24).IsRequired();
        b.Property(x => x.AvatarKey).HasMaxLength(300);
        b.HasIndex(x => x.UserName).IsUnique();

        // 1:1 with the identity user, same key
        b.HasOne<ApplicationUser>().WithOne().HasForeignKey<UserProfile>(x => x.Id).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Progress).WithOne().HasForeignKey<UserProgress>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserProgressConfiguration : IEntityTypeConfiguration<UserProgress>
{
    public void Configure(EntityTypeBuilder<UserProgress> b)
    {
        b.ToTable("UserProgress", t =>
        {
            t.HasCheckConstraint("CK_UserProgress_Xp", "[TotalXp] >= 0");
            t.HasCheckConstraint("CK_UserProgress_Streak", "[CurrentStreak] >= 0 AND [LongestStreak] >= [CurrentStreak]");
        });
        b.HasKey(x => x.UserId);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.Property(x => x.Level).HasDefaultValue(1);
        b.HasIndex(x => x.TotalXp);
    }
}

internal sealed class XpTransactionConfiguration : IEntityTypeConfiguration<XpTransaction>
{
    public void Configure(EntityTypeBuilder<XpTransaction> b)
    {
        b.ToTable("XpTransactions", t => t.HasCheckConstraint("CK_XpTransactions_Amount", "[Amount] > 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.SourceType).HasConversion<string>().HasMaxLength(32).IsRequired();
        b.Property(x => x.SourceId).HasMaxLength(64);
        b.Property(x => x.Reason).HasMaxLength(200).IsRequired();
        b.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
        // the unique key is what makes a double reward impossible, even under parallel requests
        b.HasIndex(x => x.IdempotencyKey).IsUnique();
        b.HasIndex(x => new { x.UserId, x.CreatedAtUtc, x.Id });
        b.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class DailyColorChallengeConfiguration : IEntityTypeConfiguration<DailyColorChallenge>
{
    public void Configure(EntityTypeBuilder<DailyColorChallenge> b)
    {
        b.ToTable("DailyColorChallenges");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(64).IsRequired();
        b.Property(x => x.Hex).HasMaxLength(7).IsRequired();
        b.HasIndex(x => x.Date).IsUnique();
    }
}

internal sealed class ColorHuntAttemptConfiguration : IEntityTypeConfiguration<ColorHuntAttempt>
{
    public void Configure(EntityTypeBuilder<ColorHuntAttempt> b)
    {
        b.ToTable("ColorHuntAttempts", t => t.HasCheckConstraint("CK_ColorHuntAttempts_Score", "[Score] >= 0 AND [Score] <= 1000"));
        b.HasKey(x => x.Id);
        b.Property(x => x.TargetHex).HasMaxLength(7).IsRequired();
        b.Property(x => x.FoundHex).HasMaxLength(7).IsRequired();
        b.Property(x => x.PhotoKey).HasMaxLength(300).IsRequired();

        b.HasOne(x => x.Challenge).WithMany().HasForeignKey(x => x.ChallengeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.UserId, x.ChallengeId, x.Score });
        b.HasIndex(x => new { x.ChallengeId, x.Score });
    }
}

internal sealed class PostConfiguration : IEntityTypeConfiguration<Post>
{
    public void Configure(EntityTypeBuilder<Post> b)
    {
        b.ToTable("Posts");
        b.HasKey(x => x.Id);
        b.Property(x => x.Caption).HasMaxLength(500);
        b.Property(x => x.ColorHex).HasMaxLength(7);
        b.Property(x => x.ColorFamily).HasMaxLength(16);
        b.HasIndex(x => new { x.ColorFamily, x.CreatedAtUtc });
        b.Property(x => x.Type).HasConversion<int>();

        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Media).WithOne().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Likes).WithOne().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Comments).WithOne().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);

        // feed keyset pagination (newest first) + per-author timeline
        b.HasIndex(x => new { x.CreatedAtUtc, x.Id }).IsDescending(true, true);
        b.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
    }
}

internal sealed class PostMediaConfiguration : IEntityTypeConfiguration<PostMedia>
{
    public void Configure(EntityTypeBuilder<PostMedia> b)
    {
        b.ToTable("PostMedia");
        b.HasKey(x => x.Id);
        b.Property(x => x.StorageKey).HasMaxLength(300).IsRequired();
        b.HasIndex(x => new { x.PostId, x.SortOrder }).IsUnique();
        b.HasIndex(x => x.StorageKey);
    }
}

internal sealed class PostLikeConfiguration : IEntityTypeConfiguration<PostLike>
{
    public void Configure(EntityTypeBuilder<PostLike> b)
    {
        b.ToTable("PostLikes");
        b.HasKey(x => new { x.PostId, x.UserId }); // one like per user per post
        // NoAction: avoids multiple cascade paths (Profile -> Post -> Like)
        b.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => x.UserId);
    }
}

internal sealed class PostCommentConfiguration : IEntityTypeConfiguration<PostComment>
{
    public void Configure(EntityTypeBuilder<PostComment> b)
    {
        b.ToTable("PostComments");
        b.HasKey(x => x.Id);
        b.Property(x => x.Text).HasMaxLength(500).IsRequired();
        b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => new { x.PostId, x.CreatedAtUtc, x.Id });
    }
}

internal sealed class FriendshipConfiguration : IEntityTypeConfiguration<Friendship>
{
    public void Configure(EntityTypeBuilder<Friendship> b)
    {
        b.ToTable("Friendships", t => t.HasCheckConstraint("CK_Friendships_NotSelf", "[RequesterId] <> [AddresseeId]"));
        b.HasKey(x => x.Id);
        b.Property(x => x.PairKey).HasMaxLength(65).IsRequired();
        b.Property(x => x.Status).HasConversion<int>();
        // one row per unordered pair, so A→B and B→A can never both exist
        b.HasIndex(x => x.PairKey).IsUnique();
        b.HasIndex(x => new { x.AddresseeId, x.Status });
        b.HasIndex(x => new { x.RequesterId, x.Status });
        // NoAction: two FKs to the same table would otherwise create multiple cascade paths
        b.HasOne(x => x.Requester).WithMany().HasForeignKey(x => x.RequesterId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.Addressee).WithMany().HasForeignKey(x => x.AddresseeId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class DailyHuntConfiguration : IEntityTypeConfiguration<DailyHunt>
{
    public void Configure(EntityTypeBuilder<DailyHunt> b)
    {
        b.ToTable("DailyHunts", t =>
        {
            t.HasCheckConstraint("CK_DailyHunts_Xp", "[BaseXp] >= 0");
            t.HasCheckConstraint("CK_DailyHunts_MinScore", "[MinScore] >= 0 AND [MinScore] <= 1000");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Difficulty).HasConversion<string>().HasMaxLength(8).IsRequired();
        b.Property(x => x.Name).HasMaxLength(64).IsRequired();
        b.Property(x => x.TargetColorHex).HasMaxLength(7).IsRequired();
        // exactly one hunt per difficulty and day: this also makes "create today's hunts" safe under parallel requests
        b.HasIndex(x => new { x.ChallengeDate, x.Difficulty }).IsUnique();
    }
}

internal sealed class DailyHuntAttemptConfiguration : IEntityTypeConfiguration<DailyHuntAttempt>
{
    public void Configure(EntityTypeBuilder<DailyHuntAttempt> b)
    {
        b.ToTable("DailyHuntAttempts", t => t.HasCheckConstraint("CK_DailyHuntAttempts_Score", "[Score] >= 0 AND [Score] <= 1000"));
        b.HasKey(x => x.Id);
        b.Property(x => x.SampledColorHex).HasMaxLength(7).IsRequired();
        b.Property(x => x.PhotoKey).HasMaxLength(300).IsRequired();
        b.Property(x => x.PhotoHash).HasMaxLength(64).IsRequired();

        b.HasOne(x => x.DailyHunt).WithMany().HasForeignKey(x => x.DailyHuntId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.UserId, x.DailyHuntId, x.Score });
        b.HasIndex(x => new { x.DailyHuntId, x.Score });
        b.HasIndex(x => new { x.UserId, x.PhotoHash });
    }
}

internal sealed class UserAtlasColorConfiguration : IEntityTypeConfiguration<UserAtlasColor>
{
    public void Configure(EntityTypeBuilder<UserAtlasColor> b)
    {
        b.ToTable("UserAtlasColors");
        b.HasKey(x => x.Id);
        b.Property(x => x.ColorKey).HasMaxLength(32).IsRequired();
        b.Property(x => x.CollectionKey).HasMaxLength(32).IsRequired();
        b.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        // a colour can be unlocked only once per player, even under parallel requests
        b.HasIndex(x => new { x.UserId, x.ColorKey }).IsUnique();
        b.HasIndex(x => new { x.UserId, x.CollectionKey });
    }
}

internal sealed class BingoBoardConfiguration : IEntityTypeConfiguration<BingoBoard>
{
    public void Configure(EntityTypeBuilder<BingoBoard> b)
    {
        b.ToTable("BingoBoards");
        b.HasKey(x => x.Id);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Cells).WithOne(c => c.Board).HasForeignKey(c => c.BoardId).OnDelete(DeleteBehavior.Cascade);
        // one active board per player, even when two requests create one at the same moment
        b.HasIndex(x => x.UserId).IsUnique().HasFilter("[Status] = 'Active'").HasDatabaseName("IX_BingoBoards_UserId_Active");
        b.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
    }
}

internal sealed class BingoCellConfiguration : IEntityTypeConfiguration<BingoCell>
{
    public void Configure(EntityTypeBuilder<BingoCell> b)
    {
        b.ToTable("BingoCells", t => t.HasCheckConstraint("CK_BingoCells_Index", "[Index] >= 0 AND [Index] <= 8"));
        b.HasKey(x => x.Id);
        b.Property(x => x.ColorKey).HasMaxLength(32).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasIndex(x => new { x.BoardId, x.Index }).IsUnique();
    }
}

internal sealed class BingoAttemptConfiguration : IEntityTypeConfiguration<BingoAttempt>
{
    public void Configure(EntityTypeBuilder<BingoAttempt> b)
    {
        b.ToTable("BingoAttempts", t => t.HasCheckConstraint("CK_BingoAttempts_Score", "[Score] >= 0 AND [Score] <= 1000"));
        b.HasKey(x => x.Id);
        b.Property(x => x.SampledColorHex).HasMaxLength(7).IsRequired();
        b.Property(x => x.PhotoHash).HasMaxLength(64).IsRequired();
        b.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<BingoBoard>().WithMany().HasForeignKey(x => x.BoardId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.BoardId, x.CellIndex });
        b.HasIndex(x => new { x.BoardId, x.PhotoHash });
    }
}

internal sealed class DuelConfiguration : IEntityTypeConfiguration<Duel>
{
    public void Configure(EntityTypeBuilder<Duel> b)
    {
        b.ToTable("Duels");
        b.HasKey(x => x.Id);
        b.Property(x => x.ColorKey).HasMaxLength(32).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne(x => x.Challenger).WithMany().HasForeignKey(x => x.ChallengerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Opponent).WithMany().HasForeignKey(x => x.OpponentId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.ChallengerId, x.Status });
        b.HasIndex(x => new { x.OpponentId, x.Status });
        b.HasIndex(x => new { x.Status, x.ExpiresAtUtc });
    }
}

internal sealed class DuelAttemptConfiguration : IEntityTypeConfiguration<DuelAttempt>
{
    public void Configure(EntityTypeBuilder<DuelAttempt> b)
    {
        b.ToTable("DuelAttempts", t => t.HasCheckConstraint("CK_DuelAttempts_Score", "[Score] >= 0 AND [Score] <= 1000"));
        b.HasKey(x => x.Id);
        b.Property(x => x.SampledColorHex).HasMaxLength(7).IsRequired();
        b.Property(x => x.PhotoHash).HasMaxLength(64).IsRequired();
        b.HasOne<Duel>().WithMany().HasForeignKey(x => x.DuelId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<UserProfile>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.DuelId, x.UserId });
        b.HasIndex(x => new { x.DuelId, x.PhotoHash });
    }
}
