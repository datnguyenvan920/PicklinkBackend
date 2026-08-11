using Microsoft.EntityFrameworkCore;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Repositories;
using PicklinkBackend.Services.Bookings;
using PicklinkBackend.Services.Shared;

namespace PicklinkBackend.Services.Bookings.Implementations;

public sealed class PlayerBookingReviewService
{
    private readonly IBookingRepository _bookingRepository;

    public PlayerBookingReviewService(IBookingRepository bookingRepository)
    {
        _bookingRepository = bookingRepository;
    }

    public async Task<PlayerBookingReviewResult> GetAsync(
        int bookingId,
        int? userId,
        CancellationToken cancellationToken)
    {
        if (userId is null) return PlayerBookingReviewResult.Unauthorized();

        var review = await _bookingRepository.GetBookingRatingAsync(bookingId, userId.Value, cancellationToken);

        return review is null
            ? PlayerBookingReviewResult.NotFound("Booking chưa có đánh giá.")
            : PlayerBookingReviewResult.Success(MapReview(review));
    }

    public async Task<PlayerBookingReviewResult> CreateAsync(
        int bookingId,
        CreateBookingReviewRequest request,
        int? userId,
        CancellationToken cancellationToken)
    {
        if (userId is null) return PlayerBookingReviewResult.Unauthorized();

        var booking = await _bookingRepository.GetBookingForReviewAsync(bookingId, userId.Value, cancellationToken);
        if (booking is null) return PlayerBookingReviewResult.NotFound("Không tìm thấy booking thuộc tài khoản của bạn.");

        var isEligible = booking.Status == "Completed"
            || booking.Operation?.CheckInStatus == "CheckedIn"
            || booking.Status == "Confirmed" && booking.EndTime <= VietnamTime.Now;
        if (!isEligible)
        {
            return PlayerBookingReviewResult.Conflict(
                "Chỉ được đánh giá khi BookingStatus = Completed, booking Confirmed đã kết thúc hoặc CheckInStatus = CheckedIn.");
        }

        if (booking.Ratings.Any(item => item.UserId == userId.Value))
            return PlayerBookingReviewResult.Conflict("Bạn đã đánh giá booking này rồi.");

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

        await _bookingRepository.AddRatingAsync(review, cancellationToken);
        try
        {
            await _bookingRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            var existing = await _bookingRepository.GetBookingRatingAsync(bookingId, userId.Value, cancellationToken);
            if (existing != null)
            {
                return PlayerBookingReviewResult.Conflict("Bạn đã đánh giá booking này rồi.");
            }

            throw;
        }

        await _bookingRepository.UpdateVenueOverallRatingAsync(booking.Court.VenueId, cancellationToken);

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
