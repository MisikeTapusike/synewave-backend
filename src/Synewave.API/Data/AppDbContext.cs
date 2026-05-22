using Microsoft.EntityFrameworkCore;
using Synewave.API.Models;

namespace Synewave.API.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Track> Tracks => Set<Track>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<ListeningHistory> ListeningHistory => Set<ListeningHistory>();
    public DbSet<Collective> Collectives => Set<Collective>();
    public DbSet<CollectiveMember> CollectiveMembers => Set<CollectiveMember>();
    public DbSet<Like> Likes => Set<Like>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User
        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.Username).HasMaxLength(50);
        });

        // Track
        modelBuilder.Entity<Track>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.SpotifyId);
            e.Property(x => x.Title).HasMaxLength(256);
            e.Property(x => x.Artist).HasMaxLength(256);
        });

        // Friendship - prevent duplicate friendships
        modelBuilder.Entity<Friendship>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.RequesterId, x.AddresseeId }).IsUnique();

            e.HasOne(x => x.Requester)
             .WithMany(u => u.SentFriendRequests)
             .HasForeignKey(x => x.RequesterId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Addressee)
             .WithMany(u => u.ReceivedFriendRequests)
             .HasForeignKey(x => x.AddresseeId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ListeningHistory
        modelBuilder.Entity<ListeningHistory>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.PlayedAt });

            e.HasOne(x => x.User)
             .WithMany(u => u.ListeningHistory)
             .HasForeignKey(x => x.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Track)
             .WithMany(t => t.ListeningHistory)
             .HasForeignKey(x => x.TrackId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // Collective
        modelBuilder.Entity<Collective>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100);
        });

        // CollectiveMember - unique per collective
        modelBuilder.Entity<CollectiveMember>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.CollectiveId, x.UserId }).IsUnique();

            e.HasOne(x => x.Collective)
             .WithMany(c => c.Members)
             .HasForeignKey(x => x.CollectiveId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.User)
             .WithMany(u => u.CollectiveMemberships)
             .HasForeignKey(x => x.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // Like - one like per user per track
        modelBuilder.Entity<Like>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.TrackId }).IsUnique();

            e.HasOne(x => x.User)
             .WithMany(u => u.Likes)
             .HasForeignKey(x => x.UserId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Track)
             .WithMany(t => t.Likes)
             .HasForeignKey(x => x.TrackId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // Notification
        modelBuilder.Entity<Notification>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.IsRead });

            e.HasOne(x => x.User)
             .WithMany()
             .HasForeignKey(x => x.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
