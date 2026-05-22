namespace Synewave.API.DTOs;

// ── AUTH ──────────────────────────────────────────────────────
public record RegisterRequest(string Username, string Email, string Password);
public record LoginRequest(string Email, string Password);
public record AuthResponse(string Token, UserDto User);

// ── USER ──────────────────────────────────────────────────────
public record UserDto(
    Guid Id,
    string Username,
    string Email,
    string? AvatarUrl,
    DateTime LastActiveAt
);

public record UserPublicDto(
    Guid Id,
    string Username,
    string? AvatarUrl,
    bool IsOnline,
    string? CurrentTrack,   // "Song - Artist" or null
    int CompatibilityScore  // 0-100 taste match
);

// ── TRACK ─────────────────────────────────────────────────────
public record TrackDto(
    Guid Id,
    string Title,
    string Artist,
    string? Album,
    string? CoverUrl,
    int DurationSeconds,
    string? Genre,
    bool IsLiked
);

public record CreateTrackRequest(
    string Title,
    string Artist,
    string? Album,
    string? CoverUrl,
    string? SpotifyId,
    int DurationSeconds,
    string? Genre
);

public record LogPlayRequest(
    Guid TrackId,
    int SecondsListened,
    string? Source
);

// ── FRIENDS ───────────────────────────────────────────────────
public record FriendDto(
    Guid FriendshipId,
    UserPublicDto User,
    string Status,
    DateTime Since
);

public record FriendActivityDto(
    Guid UserId,
    string Username,
    string? AvatarUrl,
    string? CurrentSong,
    string? CurrentArtist,
    DateTime? LastPlayedAt,
    bool IsOnline
);

// ── COLLECTIVES ───────────────────────────────────────────────
public record CollectiveDto(
    Guid Id,
    string Name,
    string? Description,
    string Emoji,
    string Color,
    int MemberCount,
    List<UserPublicDto> Members
);

public record CreateCollectiveRequest(
    string Name,
    string? Description,
    string Emoji,
    string Color,
    List<Guid> InvitedUserIds
);

// ── LISTENING HISTORY ─────────────────────────────────────────
public record ListeningHistoryDto(
    Guid Id,
    TrackDto Track,
    DateTime PlayedAt,
    int SecondsListened
);

// ── FEED ──────────────────────────────────────────────────────
public record FeedItemDto(
    Guid UserId,
    string Username,
    string? AvatarUrl,
    TrackDto Track,
    DateTime PlayedAt,
    string TimeAgo
);

// ── RECOMMENDATIONS ───────────────────────────────────────────
public record RecommendationDto(
    TrackDto Track,
    string Reason,           // "Na základe vkusu Jakuba"
    string ReasonType,       // "friend", "group", "genre"
    double Score
);

// ── STATS ─────────────────────────────────────────────────────
public record UserStatsDto(
    int TotalTracksPlayed,
    int TotalHoursListened,
    int UniqueArtists,
    int UniqueGenres,
    List<TrackDto> TopTracks,
    List<string> TopGenres,
    List<DayActivityDto> WeeklyActivity
);

public record DayActivityDto(string Day, int Minutes);

// ── NOTIFICATIONS ─────────────────────────────────────────────
public record NotificationDto(
    Guid Id,
    string Type,
    string Message,
    string? ActionUrl,
    bool IsRead,
    DateTime CreatedAt
);

// ── GENERIC ───────────────────────────────────────────────────
public record ApiResponse<T>(bool Success, T? Data, string? Error = null);
public record PagedResponse<T>(List<T> Items, int Total, int Page, int PageSize);
