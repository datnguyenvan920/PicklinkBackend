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
        if (match.Status != "Completed")
            return Conflict(new { message = "Chỉ được đánh giá sau khi trận đã hoàn thành." });

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
        var receivedScores = await _matchRepository.Matches
            .AsNoTracking()
            .SelectMany(item => item.MatchPlayerReviews)
            .Where(item => item.RevieweePlayerId == revieweePlayerId)
            .Select(item => item.Score)
            .ToListAsync(cancellationToken);
        reviewee.Player.Prestige = PlayerPrestige.Average(
            receivedScores.Sum() + request.Score,
            receivedScores.Count + 1);
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
