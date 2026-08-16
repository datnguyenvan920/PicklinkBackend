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

        // A match booking is checked in by scanning each player against one check-in code, so only
        // members who turned up for a code on this very round may rate it.
        if (booking.MatchId.HasValue && !await _bookingRepository.Bookings
            .AsNoTracking()
            .Where(item => item.BookingId == booking.BookingId)
            .SelectMany(item => item.Match!.MatchCheckIns)
            .AnyAsync(checkIn => checkIn.Player.UserId == userId.Value
                && checkIn.Status == "Present"
                && checkIn.BookingCheckInGroup != null
                && checkIn.BookingCheckInGroup.BookingId == booking.BookingId, cancellationToken))
        {
            return PlayerBookingReviewResult.Conflict("Chỉ người đã check-in tại sân mới được đánh giá.");
        }

        // One rating per venue per person, for good: playing there again only ever edits it.
        if (await _bookingRepository.GetVenueRatingAsync(
                booking.Court.VenueId, userId.Value, tracking: false, cancellationToken) is not null)
        {
            return PlayerBookingReviewResult.Conflict("Bạn đã đánh giá sân này rồi, hãy sửa đánh giá cũ.");
        }

        var tags = NormalizeTags(request.Tags);
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

    /// <summary>
    /// Ratings are addressed by venue rather than by booking: a player keeps exactly one rating
    /// per venue and revising it is the only way to change what they said about the place.
    /// </summary>
    public async Task<PlayerBookingReviewResult> GetVenueAsync(
        int venueId,
        int? userId,
        CancellationToken cancellationToken)
    {
        if (userId is null) return PlayerBookingReviewResult.Unauthorized();

        var review = await _bookingRepository.GetVenueRatingAsync(
            venueId, userId.Value, tracking: false, cancellationToken);

        return review is null
            ? PlayerBookingReviewResult.NotFound("Bạn chưa đánh giá sân này.")
            : PlayerBookingReviewResult.Success(MapReview(review));
    }

    public async Task<PlayerBookingReviewResult> UpdateVenueAsync(
        int venueId,
        CreateBookingReviewRequest request,
        int? userId,
        CancellationToken cancellationToken)
    {
        if (userId is null) return PlayerBookingReviewResult.Unauthorized();

        var review = await _bookingRepository.GetVenueRatingAsync(
            venueId, userId.Value, tracking: true, cancellationToken);
        if (review is null)
            return PlayerBookingReviewResult.NotFound("Bạn chưa đánh giá sân này nên không có gì để sửa.");

        review.Score = request.Score;
        review.Comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
        review.Tags = NormalizeTags(request.Tags) is { Count: > 0 } tags ? string.Join('|', tags) : null;
        review.IsAnonymous = request.IsAnonymous;

        await _bookingRepository.SaveChangesAsync(cancellationToken);
        await _bookingRepository.UpdateVenueOverallRatingAsync(venueId, cancellationToken);

        return PlayerBookingReviewResult.Success(MapReview(review));
    }

    private static List<string> NormalizeTags(List<string> tags) => tags
        .Select(item => item.Trim())
        .Where(item => item.Length > 0)
        .Select(item => item.Length > 50 ? item[..50] : item)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Take(10)
        .ToList();

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
