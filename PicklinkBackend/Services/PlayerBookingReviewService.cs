using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;

namespace PicklinkBackend.Services;

public sealed class PlayerBookingReviewService
{
    private readonly ApplicationDbContext _dbContext;

    public PlayerBookingReviewService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PlayerBookingReviewResult> GetAsync(
        int bookingId,
        int? userId,
        CancellationToken cancellationToken)
    {
        if (userId is null) return PlayerBookingReviewResult.Unauthorized();

        var review = await _dbContext.RatingHistories.AsNoTracking()
            .Include(item => item.Booking).ThenInclude(item => item!.Court).ThenInclude(item => item.Venue)
            .SingleOrDefaultAsync(item => item.BookingId == bookingId && item.UserId == userId.Value, cancellationToken);

        return review is null
            ? PlayerBookingReviewResult.NotFound("Booking chua co danh gia.")
            : PlayerBookingReviewResult.Success(MapReview(review));
    }

    public async Task<PlayerBookingReviewResult> CreateAsync(
        int bookingId,
        CreateBookingReviewRequest request,
        int? userId,
        CancellationToken cancellationToken)
    {
        if (userId is null) return PlayerBookingReviewResult.Unauthorized();

        var booking = await _dbContext.Bookings
            .Include(item => item.Player)
            .Include(item => item.Court).ThenInclude(item => item.Venue)
            .Include(item => item.Operation)
            .Include(item => item.Ratings)
            .SingleOrDefaultAsync(item => item.BookingId == bookingId && item.Player!.UserId == userId.Value, cancellationToken);
        if (booking is null) return PlayerBookingReviewResult.NotFound("Khong tim thay booking thuoc tai khoan cua ban.");

        var isEligible = booking.Status == "Completed" || booking.Operation?.CheckInStatus == "CheckedIn";
        if (!isEligible)
        {
            return PlayerBookingReviewResult.Conflict(
                "Chi duoc danh gia khi BookingStatus = Completed hoac CheckInStatus = CheckedIn.");
        }

        if (booking.Ratings.Any(item => item.UserId == userId.Value))
            return PlayerBookingReviewResult.Conflict("Ban da danh gia booking nay roi.");

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
            {
                return PlayerBookingReviewResult.Conflict("Ban da danh gia booking nay roi.");
            }

            throw;
        }

        booking.Court.Venue.OverallRating = await _dbContext.RatingHistories.AsNoTracking()
            .Where(item => item.TargetType == "Venue" && item.TargetId == booking.Court.VenueId)
            .AverageAsync(item => (double)item.Score, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return PlayerBookingReviewResult.Created(MapReview(review, booking));
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
}

public sealed record PlayerBookingReviewResult(
    PlayerBookingReviewResultStatus Status,
    BookingReviewResponse? Review,
    string? ErrorMessage)
{
    public static PlayerBookingReviewResult Success(BookingReviewResponse review) =>
        new(PlayerBookingReviewResultStatus.Success, review, ErrorMessage: null);

    public static PlayerBookingReviewResult Created(BookingReviewResponse review) =>
        new(PlayerBookingReviewResultStatus.Created, review, ErrorMessage: null);

    public static PlayerBookingReviewResult Unauthorized() =>
        new(PlayerBookingReviewResultStatus.Unauthorized, Review: null, ErrorMessage: null);

    public static PlayerBookingReviewResult NotFound(string errorMessage) =>
        new(PlayerBookingReviewResultStatus.NotFound, Review: null, errorMessage);

    public static PlayerBookingReviewResult Conflict(string errorMessage) =>
        new(PlayerBookingReviewResultStatus.Conflict, Review: null, errorMessage);
}

public enum PlayerBookingReviewResultStatus
{
    Success,
    Created,
    Unauthorized,
    NotFound,
    Conflict
}