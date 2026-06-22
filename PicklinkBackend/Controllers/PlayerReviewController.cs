using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize]
[Route("api/player-reviews")]
public class PlayerReviewController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public PlayerReviewController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("booking/{bookingId:int}")]
    public async Task<ActionResult<BookingReviewResponse>> GetBookingReview(
        int bookingId,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        var review = await _dbContext.RatingHistories.AsNoTracking()
            .Include(item => item.Booking).ThenInclude(item => item!.Court).ThenInclude(item => item.Venue)
            .SingleOrDefaultAsync(item => item.BookingId == bookingId && item.UserId == userId.Value, cancellationToken);
        return review is null
            ? NotFound(new { message = "Booking chưa có đánh giá." })
            : Ok(MapReview(review));
    }

    [HttpPost("booking/{bookingId:int}")]
    public async Task<ActionResult<BookingReviewResponse>> CreateBookingReview(
        int bookingId,
        CreateBookingReviewRequest request,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        var booking = await _dbContext.Bookings
            .Include(item => item.Player)
            .Include(item => item.Court).ThenInclude(item => item.Venue)
            .Include(item => item.Operation)
            .Include(item => item.Ratings)
            .SingleOrDefaultAsync(item => item.BookingId == bookingId && item.Player!.UserId == userId.Value, cancellationToken);
        if (booking is null) return NotFound(new { message = "Không tìm thấy booking thuộc tài khoản của bạn." });

        var isEligible = booking.Status == "Completed" || booking.Operation?.CheckInStatus == "CheckedIn";
        if (!isEligible)
            return Conflict(new
            {
                message = "Chỉ được đánh giá khi BookingStatus = Completed hoặc CheckInStatus = CheckedIn."
            });
        if (booking.Ratings.Any(item => item.UserId == userId.Value))
            return Conflict(new { message = "Bạn đã đánh giá booking này rồi." });

        var tags = request.Tags
            .Select(item => item.Trim())
            .Where(item => item.Length > 0)
            .Select(item => item.Length > 50 ? item[..50] : item)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
        var review = new RatingHistory
        {
            UserId = userId.Value,
            BookingId = booking.BookingId,
            TargetId = booking.Court.VenueId,
            TargetType = "Venue",
            Score = request.Score,
            Comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim(),
            Tags = tags.Count == 0 ? null : string.Join('|', tags),
            IsAnonymous = request.IsAnonymous,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.RatingHistories.Add(review);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _dbContext.ChangeTracker.Clear();
            if (await _dbContext.RatingHistories.AsNoTracking()
                .AnyAsync(item => item.BookingId == bookingId && item.UserId == userId.Value, cancellationToken))
                return Conflict(new { message = "Bạn đã đánh giá booking này rồi." });
            throw;
        }

        booking.Court.Venue.OverallRating = await _dbContext.RatingHistories.AsNoTracking()
            .Where(item => item.TargetType == "Venue" && item.TargetId == booking.Court.VenueId)
            .AverageAsync(item => (double)item.Score, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetBookingReview), new { bookingId }, MapReview(review, booking));
    }

    private static BookingReviewResponse MapReview(RatingHistory review, Booking? booking = null)
    {
        var sourceBooking = booking ?? review.Booking!;
        return new BookingReviewResponse
        {
            RatingId = review.RatingId,
            BookingId = sourceBooking.BookingId,
            VenueId = sourceBooking.Court.VenueId,
            VenueName = sourceBooking.Court.Venue.VenueName,
            Score = review.Score,
            Comment = review.Comment,
            Tags = string.IsNullOrWhiteSpace(review.Tags)
                ? []
                : review.Tags.Split('|', StringSplitOptions.RemoveEmptyEntries).ToList(),
            IsAnonymous = review.IsAnonymous,
            CreatedAt = DateTime.SpecifyKind(review.CreatedAt, DateTimeKind.Utc)
        };
    }

    private int? CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
