namespace Synewave.API.Models;

public class Track
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string? Album { get; set; }
    public string? CoverUrl { get; set; }
    public string? SpotifyId { get; set; }
    public string? AppleMusicId { get; set; }
    public int DurationSeconds { get; set; }
    public string? Genre { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ListeningHistory> ListeningHistory { get; set; } = new List<ListeningHistory>();
    public ICollection<Like> Likes { get; set; } = new List<Like>();
}

public class Friendship
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequesterId { get; set; }
    public Guid AddresseeId { get; set; }
    public FriendshipStatus Status { get; set; } = FriendshipStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User Requester { get; set; } = null!;
    public User Addressee { get; set; } = null!;
}

public enum FriendshipStatus { Pending, Accepted, Rejected, Blocked }

public class ListeningHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid TrackId { get; set; }
    public DateTime PlayedAt { get; set; } = DateTime.UtcNow;
    public int SecondsListened { get; set; }
    public string? Source { get; set; } // "spotify", "apple_music", "synewave"

    public User User { get; set; } = null!;
    public Track Track { get; set; } = null!;
}

public class Collective
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Emoji { get; set; } = "🎵";
    public string Color { get; set; } = "#7c5cfc";
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<CollectiveMember> Members { get; set; } = new List<CollectiveMember>();
}

public class CollectiveMember
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CollectiveId { get; set; }
    public Guid UserId { get; set; }
    public CollectiveRole Role { get; set; } = CollectiveRole.Member;
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    public Collective Collective { get; set; } = null!;
    public User User { get; set; } = null!;
}

public enum CollectiveRole { Member, Admin }

public class Like
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid TrackId { get; set; }
    public DateTime LikedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public Track Track { get; set; } = null!;
}

public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Type { get; set; } = string.Empty; // "friend_request", "like", "collective_invite"
    public string Message { get; set; } = string.Empty;
    public string? ActionUrl { get; set; }
    public bool IsRead { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
}
