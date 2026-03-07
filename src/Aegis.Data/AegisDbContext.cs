using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Aegis.Data.Entities;

namespace Aegis.Data;

/// <summary>
/// Entity Framework Core DbContext for Aegis Messenger
/// </summary>
public class AegisDbContext : DbContext
{
    private const string DefaultTimestampSql = "CURRENT_TIMESTAMP";

    public AegisDbContext(DbContextOptions<AegisDbContext> options) : base(options)
    {
    }

    // Parameterless constructor for design-time tools
    public AegisDbContext()
    {
    }

    public DbSet<User> Users { get; set; }
    public DbSet<Session> Sessions { get; set; }
    public DbSet<Message> Messages { get; set; }
    public DbSet<Group> Groups { get; set; }
    public DbSet<GroupMember> GroupMembers { get; set; }
    public DbSet<GroupMessage> GroupMessages { get; set; }
    public DbSet<PreKey> PreKeys { get; set; }
    public DbSet<Device> Devices { get; set; }
    public DbSet<Channel> Channels { get; set; }
    public DbSet<ChannelMember> ChannelMembers { get; set; }
    public DbSet<ChannelMessage> ChannelMessages { get; set; }
    public DbSet<PrivateChat> PrivateChats { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            // Default configuration for design-time tools
            optionsBuilder.UseSqlite("Data Source=aegis.db");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User configuration
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Username).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql(DefaultTimestampSql);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql(DefaultTimestampSql);
        });

        // Session configuration
        modelBuilder.Entity<Session>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.SessionToken).IsUnique();
            entity.HasOne(e => e.User).WithMany(u => u.Sessions).HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql(DefaultTimestampSql);
        });

        // Message configuration
        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.FromUserId, e.CreatedAt });
            entity.HasIndex(e => new { e.ToUserId, e.IsRead });
            entity.HasOne(e => e.FromUser).WithMany(u => u.SentMessages).HasForeignKey(e => e.FromUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.ToUser).WithMany(u => u.ReceivedMessages).HasForeignKey(e => e.ToUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql(DefaultTimestampSql);
        });

        // Group configuration
        modelBuilder.Entity<Group>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name);
            entity.HasOne(e => e.CreatedByUser).WithMany().HasForeignKey(e => e.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql(DefaultTimestampSql);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql(DefaultTimestampSql);
        });

        // GroupMember configuration
        modelBuilder.Entity<GroupMember>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.GroupId, e.UserId }).IsUnique();
            entity.HasOne(e => e.Group).WithMany(g => g.Members).HasForeignKey(e => e.GroupId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.User).WithMany(u => u.GroupMemberships).HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(e => e.JoinedAt).HasDefaultValueSql(DefaultTimestampSql);
        });

        // GroupMessage configuration
        modelBuilder.Entity<GroupMessage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.GroupId, e.CreatedAt });
            entity.HasOne(e => e.Group).WithMany(g => g.Messages).HasForeignKey(e => e.GroupId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.FromUser).WithMany().HasForeignKey(e => e.FromUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.ReplyToMessage).WithMany(m => m.Replies).HasForeignKey(e => e.ReplyToMessageId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql(DefaultTimestampSql);
        });

        // PreKey configuration
        modelBuilder.Entity<PreKey>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.KeyId }).IsUnique();
        });

        // Device configuration
        modelBuilder.Entity<Device>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.DeviceId).IsUnique();
            entity.HasOne(e => e.User).WithMany(u => u.Devices).HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql(DefaultTimestampSql);
        });

        // Channel configuration
        modelBuilder.Entity<Channel>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name);
            entity.HasOne(e => e.CreatedByUser).WithMany(u => u.CreatedChannels).HasForeignKey(e => e.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql(DefaultTimestampSql);
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql(DefaultTimestampSql);
        });

        // ChannelMember configuration
        modelBuilder.Entity<ChannelMember>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ChannelId, e.UserId }).IsUnique();
            entity.HasOne(e => e.Channel).WithMany(c => c.Members).HasForeignKey(e => e.ChannelId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.User).WithMany(u => u.ChannelMemberships).HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(e => e.JoinedAt).HasDefaultValueSql(DefaultTimestampSql);
        });

        // ChannelMessage configuration
        modelBuilder.Entity<ChannelMessage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ChannelId, e.CreatedAt });
            entity.HasOne(e => e.Channel).WithMany(c => c.Messages).HasForeignKey(e => e.ChannelId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.FromUser).WithMany().HasForeignKey(e => e.FromUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.ReplyToMessage).WithMany(m => m.Replies).HasForeignKey(e => e.ReplyToMessageId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql(DefaultTimestampSql);
        });

        // PrivateChat configuration
        modelBuilder.Entity<PrivateChat>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.User1Id, e.User2Id }).IsUnique();
            entity.HasOne(e => e.User1).WithMany(u => u.PrivateChats1).HasForeignKey(e => e.User1Id)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.User2).WithMany(u => u.PrivateChats2).HasForeignKey(e => e.User2Id)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.LastMessage).WithMany().HasForeignKey(e => e.LastMessageId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql(DefaultTimestampSql);
        });
    }
}
