using Microsoft.EntityFrameworkCore;
using Synewave.API.Data;
using Synewave.API.DTOs;
using Synewave.API.Models;

namespace Synewave.API.Services;

public interface IRecommendationService
{
    Task<List<RecommendationDto>> GetRecommendationsAsync(Guid userId, int count = 10);
    Task<int> GetCompatibilityScoreAsync(Guid userId, Guid friendId);
    Task<List<string>> GetTopGenresAsync(Guid userId);
}

public class RecommendationService : IRecommendationService
{
    private readonly AppDbContext _db;

    public RecommendationService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<RecommendationDto>> GetRecommendationsAsync(Guid userId, int count = 10)
    {
        var recommendations = new List<RecommendationDto>();

        // Get user's friends
        var friendIds = await GetFriendIdsAsync(userId);

        // Get user's already-listened track IDs (last 60 days)
        var cutoff = DateTime.UtcNow.AddDays(-60);
        var listenedTrackIds = await _db.ListeningHistory
            .Where(h => h.UserId == userId && h.PlayedAt > cutoff)
            .Select(h => h.TrackId)
            .Distinct()
            .ToListAsync();

        // Strategy 1: Tracks friends listened to but user hasn't
        var friendTracks = await _db.ListeningHistory
            .Where(h => friendIds.Contains(h.UserId)
                     && !listenedTrackIds.Contains(h.TrackId)
                     && h.PlayedAt > DateTime.UtcNow.AddDays(-14))
            .Include(h => h.Track)
            .Include(h => h.User)
            .GroupBy(h => h.TrackId)
            .Select(g => new
            {
                Track = g.First().Track,
                FriendName = g.First().User.Username,
                FriendCount = g.Select(x => x.UserId).Distinct().Count(),
                Score = (double)g.Count() * g.Select(x => x.UserId).Distinct().Count()
            })
            .OrderByDescending(x => x.Score)
            .Take(count)
            .ToListAsync();

        foreach (var ft in friendTracks)
        {
            var reason = ft.FriendCount > 1
                ? $"Top u {ft.FriendCount} priateľov"
                : $"Na základe vkusu {ft.FriendName}";

            recommendations.Add(new RecommendationDto(
                MapTrack(ft.Track, false),
                reason,
                "friend",
                ft.Score
            ));
        }

        // Strategy 2: Genre-based — same genre, not yet listened
        if (recommendations.Count < count)
        {
            var userGenres = await GetTopGenresAsync(userId);
            if (userGenres.Any())
            {
                var genreTracks = await _db.Tracks
                    .Where(t => userGenres.Contains(t.Genre!)
                             && !listenedTrackIds.Contains(t.Id))
                    .OrderBy(t => Guid.NewGuid()) // random selection
                    .Take(count - recommendations.Count)
                    .ToListAsync();

                foreach (var t in genreTracks)
                {
                    recommendations.Add(new RecommendationDto(
                        MapTrack(t, false),
                        $"Tvoj obľúbený žáner: {t.Genre}",
                        "genre",
                        50
                    ));
                }
            }
        }

        return recommendations.Take(count).ToList();
    }

    public async Task<int> GetCompatibilityScoreAsync(Guid userId, Guid friendId)
    {
        var cutoff = DateTime.UtcNow.AddDays(-30);

        var userTracks = await _db.ListeningHistory
            .Where(h => h.UserId == userId && h.PlayedAt > cutoff)
            .Select(h => h.TrackId)
            .Distinct()
            .ToListAsync();

        var friendTracks = await _db.ListeningHistory
            .Where(h => h.UserId == friendId && h.PlayedAt > cutoff)
            .Select(h => h.TrackId)
            .Distinct()
            .ToListAsync();

        if (!userTracks.Any() || !friendTracks.Any()) return 0;

        // Jaccard similarity
        var intersection = userTracks.Intersect(friendTracks).Count();
        var union = userTracks.Union(friendTracks).Count();

        var jaccardScore = union > 0 ? (double)intersection / union : 0;

        // Genre similarity
        var userGenres = await GetTopGenresAsync(userId);
        var friendGenres = await GetTopGenresAsync(friendId);
        var genreIntersection = userGenres.Intersect(friendGenres).Count();
        var genreUnion = userGenres.Union(friendGenres).Count();
        var genreScore = genreUnion > 0 ? (double)genreIntersection / genreUnion : 0;

        // Weighted score
        var finalScore = (jaccardScore * 0.7 + genreScore * 0.3) * 100;
        return (int)Math.Round(Math.Min(finalScore * 2, 100)); // Scale up so it's not too low
    }

    public async Task<List<string>> GetTopGenresAsync(Guid userId)
    {
        return await _db.ListeningHistory
            .Where(h => h.UserId == userId
                     && h.PlayedAt > DateTime.UtcNow.AddDays(-30)
                     && h.Track.Genre != null)
            .Include(h => h.Track)
            .GroupBy(h => h.Track.Genre!)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => g.Key)
            .ToListAsync();
    }

    private async Task<List<Guid>> GetFriendIdsAsync(Guid userId)
    {
        var sent = await _db.Friendships
            .Where(f => f.RequesterId == userId && f.Status == FriendshipStatus.Accepted)
            .Select(f => f.AddresseeId)
            .ToListAsync();

        var received = await _db.Friendships
            .Where(f => f.AddresseeId == userId && f.Status == FriendshipStatus.Accepted)
            .Select(f => f.RequesterId)
            .ToListAsync();

        return sent.Concat(received).Distinct().ToList();
    }

    private static TrackDto MapTrack(Track t, bool isLiked) =>
        new(t.Id, t.Title, t.Artist, t.Album, t.CoverUrl, t.DurationSeconds, t.Genre, isLiked);
}
