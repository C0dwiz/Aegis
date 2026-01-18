using Microsoft.EntityFrameworkCore;
using Aegis.Data.Entities;

namespace Aegis.Data;

/// <summary>
/// Entity Framework Core DbContext for Aegis Messenger
/// </summary>
public class AegisDbContext : DbContext
{
    public AegisDbContext(DbContextOptions<AegisDbContext> options) : base(options)
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User configuration
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Username).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");
        });

        // Session configuration
        modelBuilder.Entity<Session>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.SessionToken).IsUnique();
            entity.HasOne(e => e.User).WithMany(u => u.Sessions).HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
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
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        });

        // Group configuration
        modelBuilder.Entity<Group>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name);
            entity.HasOne(e => e.CreatedByUser).WithMany().HasForeignKey(e => e.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");
        });

        // GroupMember configuration
        modelBuilder.Entity<GroupMember>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.GroupId, e.UserId }).IsUnique();
            entity.HasOne(e => e.Group).WithMany(g => g.Members).HasForeignKey(e => e.GroupId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(e => e.JoinedAt).HasDefaultValueSql("GETUTCDATE()");
        });

        // GroupMessage configuration
        modelBuilder.Entity<GroupMessage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.GroupId, e.CreatedAt });
            entity.HasOne(e => e.Group).WithMany(g => g.Messages).HasForeignKey(e => e.GroupId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
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
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        });
    }
}
