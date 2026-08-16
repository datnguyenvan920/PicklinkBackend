using Microsoft.EntityFrameworkCore;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Services.Players;
using PicklinkBackend.Services.Shared;

namespace PicklinkBackend.Services.Matches.Implementations;

public partial class MatchService
{
    public async Task<ServiceResult<OpenMatchDetailResponse>> CompleteOpenMatch(
        int matchId,
        CancellationToken cancellationToken)
    {
        var currentPlayerId = await CurrentPlayerIdAsync(cancellationToken);
        if (!currentPlayerId.HasValue) return Unauthorized();

        var match = await _matchRepository.Matches
            .Include(item => item.MatchParticipants)
            .Include(item => item.Bookings).ThenInclude(booking => booking.StatusHistories)
            .SingleOrDefaultAsync(item => item.MatchId == matchId, cancellationToken);
        if (match is null) return NotFound(new { message = "Không tìm thấy trận đấu." });
        if (!match.MatchParticipants.Any(item => item.PlayerId == currentPlayerId.Value && IsApprovedOrAccepted(item.Status)))
            return Forbid(new { message = "Chỉ thành viên chính thức của trận mới được hoàn tất trận." });

        if (match.Status != "Completed")
        {
            if (match.Status != "Booked")
                return Conflict(new { message = "Chỉ có thể hoàn tất trận đã đặt sân." });

            var activeBookings = match.Bookings
                .Where(booking => booking.Status is "Confirmed" or "Completed")
                .ToList();
            if (activeBookings.Count == 0 || activeBookings.Any(booking => booking.EndTime > VietnamTime.Now))
                return Conflict(new { message = "Chỉ có thể hoàn tất sau khi tất cả lượt chơi đã kết thúc." });

            foreach (var booking in activeBookings.Where(booking => booking.Status == "Confirmed"))
            {
                booking.Status = "Completed";
                booking.StatusHistories.Add(new BookingStatusHistory
                {
                    FromStatus = "Confirmed",
                    ToStatus = "Completed",
                    Reason = "MatchCompleted",
                    ChangedAt = DateTime.UtcNow
                });
            }

            match.Status = "Completed";
            await _matchRepository.SaveChangesAsync(cancellationToken);
            _matchRealtime.Publish(matchId, "MatchCompleted");
        }

        return Ok((await LoadOpenMatchResponseAsync(matchId, currentPlayerId, cancellationToken))!);
    }

    public async Task<ServiceResult<MatchPlayerReviewResponse>> ReviewMatchPlayer(
        int matchId,
        int revieweePlayerId,
        CreateMatchPlayerReviewRequest request,
        CancellationToken cancellationToken)
    {
        var currentPlayerId = await CurrentPlayerIdAsync(cancellationToken);
        if (!currentPlayerId.HasValue) return Unauthorized();
        if (currentPlayerId.Value == revieweePlayerId)
            return Conflict(new { message = "Bạn không thể tự đánh giá chính mình." });

        var match = await MatchReviewQuery(tracking: true)
            .SingleOrDefaultAsync(item => item.MatchId == matchId, cancellationToken);
        if (match is null) return NotFound(new { message = "Không tìm thấy trận đấu." });

        var eligibility = await CheckReviewEligibilityAsync(matchId, currentPlayerId.Value, cancellationToken);
        if (eligibility is not null) return eligibility;

        var reviewer = match.MatchParticipants.SingleOrDefault(item =>
            item.PlayerId == currentPlayerId.Value && IsApprovedOrAccepted(item.Status));
        if (reviewer is null)
            return Forbid(new { message = "Chỉ thành viên chính thức của trận mới được đánh giá." });

        var reviewee = match.MatchParticipants.SingleOrDefault(item =>
            item.PlayerId == revieweePlayerId && IsApprovedOrAccepted(item.Status));
        if (reviewee is null)
            return NotFound(new { message = "Người chơi được đánh giá không thuộc danh sách chính thức của trận." });

        if (match.MatchPlayerReviews.Any(item =>
            item.ReviewerPlayerId == currentPlayerId.Value && item.RevieweePlayerId == revieweePlayerId))
            return Conflict(new { message = "Bạn đã đánh giá người chơi này trong trận." });

        var review = new MatchPlayerReview
        {
            MatchId = matchId,
            ReviewerPlayerId = currentPlayerId.Value,
            RevieweePlayerId = revieweePlayerId,
            Score = request.Score,
            Comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim(),
            CreatedAt = DateTime.UtcNow,
            ReviewerPlayer = reviewer.Player,
            RevieweePlayer = reviewee.Player
        };
        await ApplyRevieweePrestigeAsync(
            reviewee.Player, revieweePlayerId, excludedReviewId: null, request.Score, cancellationToken);
        match.MatchPlayerReviews.Add(review);

        try
        {
            await _matchRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            var duplicate = await _matchRepository.Matches
                .AsNoTracking()
                .Where(item => item.MatchId == matchId)
                .SelectMany(item => item.MatchPlayerReviews)
                .AnyAsync(item => item.ReviewerPlayerId == currentPlayerId.Value
                    && item.RevieweePlayerId == revieweePlayerId, cancellationToken);
            if (duplicate)
                return Conflict(new { message = "Bạn đã đánh giá người chơi này trong trận." });
            throw;
        }

        _matchRealtime.Publish(matchId, "PlayerReviewed");
        return Ok(MapPlayerReview(review));
    }

    public async Task<ServiceResult<List<MatchPlayerReviewResponse>>> GetMatchPlayerReviews(
        int matchId,
        CancellationToken cancellationToken)
    {
        var currentPlayerId = await CurrentPlayerIdAsync(cancellationToken);
        if (!currentPlayerId.HasValue) return Unauthorized();

        var match = await MatchReviewQuery(tracking: false)
            .SingleOrDefaultAsync(item => item.MatchId == matchId, cancellationToken);
        if (match is null) return NotFound(new { message = "Không tìm thấy trận đấu." });
        if (!match.MatchParticipants.Any(item =>
            item.PlayerId == currentPlayerId.Value && IsApprovedOrAccepted(item.Status)))
            return Forbid(new { message = "Chỉ thành viên chính thức của trận mới xem được đánh giá của mình." });

        return Ok(match.MatchPlayerReviews
            .Where(item => item.ReviewerPlayerId == currentPlayerId.Value)
            .OrderBy(item => item.CreatedAt)
            .Select(MapPlayerReview)
            .ToList());
    }

    public async Task<ServiceResult<List<MatchPlayerReviewResponse>>> GetReceivedMatchPlayerReviews(
        CancellationToken cancellationToken)
    {
        var currentPlayerId = await CurrentPlayerIdAsync(cancellationToken);
        if (!currentPlayerId.HasValue) return Unauthorized();

        var reviews = await _matchRepository.Matches
            .AsNoTracking()
            .SelectMany(match => match.MatchPlayerReviews)
            .Where(review => review.RevieweePlayerId == currentPlayerId.Value)
            .OrderByDescending(review => review.CreatedAt)
            .Select(review => new MatchPlayerReviewResponse
            {
                MatchPlayerReviewId = review.MatchPlayerReviewId,
                MatchId = review.MatchId,
                ReviewerPlayerId = review.ReviewerPlayerId,
                ReviewerName = review.ReviewerPlayer.User.Username,
                RevieweePlayerId = review.RevieweePlayerId,
                RevieweeName = review.RevieweePlayer.User.Username,
                Score = review.Score,
                Comment = review.Comment,
                CreatedAt = DateTime.SpecifyKind(review.CreatedAt, DateTimeKind.Utc)
            })
            .ToListAsync(cancellationToken);

        return Ok(reviews);
    }

    public async Task<ServiceResult<MatchPlayerReviewResponse>> UpdateMatchPlayerReview(
        int matchId,
        int revieweePlayerId,
        CreateMatchPlayerReviewRequest request,
        CancellationToken cancellationToken)
    {
        var currentPlayerId = await CurrentPlayerIdAsync(cancellationToken);
        if (!currentPlayerId.HasValue) return Unauthorized();

        var match = await MatchReviewQuery(tracking: true)
            .SingleOrDefaultAsync(item => item.MatchId == matchId, cancellationToken);
        if (match is null) return NotFound(new { message = "Không tìm thấy trận đấu." });

        var eligibility = await CheckReviewEligibilityAsync(matchId, currentPlayerId.Value, cancellationToken);
        if (eligibility is not null) return eligibility;

        var review = match.MatchPlayerReviews.SingleOrDefault(item =>
            item.ReviewerPlayerId == currentPlayerId.Value && item.RevieweePlayerId == revieweePlayerId);
        if (review is null)
            return NotFound(new { message = "Bạn chưa đánh giá người chơi này nên không có gì để sửa." });

        var reviewee = match.MatchParticipants.SingleOrDefault(item => item.PlayerId == revieweePlayerId);
        if (reviewee is null)
            return NotFound(new { message = "Người chơi được đánh giá không thuộc danh sách chính thức của trận." });

        review.Score = request.Score;
        review.Comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
        await ApplyRevieweePrestigeAsync(
            reviewee.Player, revieweePlayerId, review.MatchPlayerReviewId, request.Score, cancellationToken);
        await _matchRepository.SaveChangesAsync(cancellationToken);

        _matchRealtime.Publish(matchId, "PlayerReviewed");
        return Ok(MapPlayerReview(review));
    }

    /// <summary>
    /// A rating belongs to the venue, not to the round or the room, so this reports whatever the
    /// player already said about the venues this match played at - even from an unrelated booking.
    /// </summary>
    public async Task<ServiceResult<List<BookingReviewResponse>>> GetMatchVenueReviews(
        int matchId,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();

        var venues = await _matchRepository.Bookings
            .AsNoTracking()
            .Where(booking => booking.MatchId == matchId)
            .Select(booking => new { booking.Court.VenueId, booking.Court.Venue.VenueName })
            .Distinct()
            .ToListAsync(cancellationToken);
        if (venues.Count == 0) return Ok(new List<BookingReviewResponse>());

        var venueIds = venues.Select(venue => venue.VenueId).ToList();
        var rows = await _matchRepository.RatingHistories
            .AsNoTracking()
            .Where(rating => rating.TargetType == "Venue"
                && venueIds.Contains(rating.TargetId)
                && rating.UserId == userId)
            .Select(rating => new
            {
                rating.RatingId,
                rating.TargetId,
                rating.BookingId,
                rating.Score,
                rating.Comment,
                rating.Tags,
                rating.IsAnonymous,
                rating.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var venueNames = venues.ToDictionary(venue => venue.VenueId, venue => venue.VenueName);
        return Ok(rows.Select(row => new BookingReviewResponse
        {
            RatingId = row.RatingId,
            BookingId = row.BookingId ?? 0,
            VenueId = row.TargetId,
            VenueName = venueNames.GetValueOrDefault(row.TargetId, string.Empty),
            Score = row.Score,
            Comment = row.Comment,
            Tags = string.IsNullOrWhiteSpace(row.Tags)
                ? []
                : row.Tags.Split('|', StringSplitOptions.RemoveEmptyEntries).ToList(),
            IsAnonymous = row.IsAnonymous,
            CreatedAt = DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc)
        }).ToList());
    }

    /// <summary>
    /// A member may rate the room once a round has been played out and they actually turned up:
    /// check-in for a match booking is a per-player scan, so absent members simply do not rate.
    /// </summary>
    private async Task<ServiceResult?> CheckReviewEligibilityAsync(
        int matchId,
        int playerId,
        CancellationToken cancellationToken)
    {
        var localNow = VietnamTime.Now;
        var hasPlayedRound = await _matchRepository.Bookings
            .AsNoTracking()
            .AnyAsync(item => item.MatchId == matchId
                && (item.Status == "Completed"
                    || (item.Status == "Confirmed" && item.EndTime <= localNow)), cancellationToken);
        if (!hasPlayedRound)
            return Conflict(new { message = "Chỉ được đánh giá sau khi lượt chơi đã kết thúc." });

        if (!await HasCheckedInAsync(matchId, playerId, cancellationToken))
            return Conflict(new { message = "Chỉ người đã check-in tại sân mới được đánh giá." });

        return null;
    }

    private Task<bool> HasCheckedInAsync(int matchId, int playerId, CancellationToken cancellationToken) =>
        _matchRepository.MatchCheckIns
            .AsNoTracking()
            .AnyAsync(item => item.MatchId == matchId
                && item.PlayerId == playerId
                && item.Status == "Present", cancellationToken);

    /// <summary>
    /// Prestige is the average of every score a player received, so an edited review has to
    /// rebuild it from the stored rows rather than fold the new score onto the old average.
    /// </summary>
    private async Task ApplyRevieweePrestigeAsync(
        Player reviewee,
        int revieweePlayerId,
        int? excludedReviewId,
        int score,
        CancellationToken cancellationToken)
    {
        var receivedScores = await _matchRepository.Matches
            .AsNoTracking()
            .SelectMany(item => item.MatchPlayerReviews)
            .Where(item => item.RevieweePlayerId == revieweePlayerId
                && (excludedReviewId == null || item.MatchPlayerReviewId != excludedReviewId.Value))
            .Select(item => item.Score)
            .ToListAsync(cancellationToken);

        reviewee.Prestige = PlayerPrestige.Average(receivedScores.Sum() + score, receivedScores.Count + 1);
    }

    private IQueryable<Match> MatchReviewQuery(bool tracking)
    {
        var query = tracking ? _matchRepository.Matches : _matchRepository.Matches.AsNoTracking();
        return query
            .AsSplitQuery()
            .Include(item => item.MatchParticipants).ThenInclude(item => item.Player).ThenInclude(item => item.User)
            .Include(item => item.MatchPlayerReviews).ThenInclude(item => item.ReviewerPlayer).ThenInclude(item => item.User)
            .Include(item => item.MatchPlayerReviews).ThenInclude(item => item.RevieweePlayer).ThenInclude(item => item.User);
    }

    private static MatchPlayerReviewResponse MapPlayerReview(MatchPlayerReview review) => new()
    {
        MatchPlayerReviewId = review.MatchPlayerReviewId,
        MatchId = review.MatchId,
        ReviewerPlayerId = review.ReviewerPlayerId,
        ReviewerName = review.ReviewerPlayer.User.Username,
        RevieweePlayerId = review.RevieweePlayerId,
        RevieweeName = review.RevieweePlayer.User.Username,
        Score = review.Score,
        Comment = review.Comment,
        CreatedAt = DateTime.SpecifyKind(review.CreatedAt, DateTimeKind.Utc)
    };
}
