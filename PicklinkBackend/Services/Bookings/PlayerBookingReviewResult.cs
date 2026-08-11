using PicklinkBackend.DTOs;

namespace PicklinkBackend.Services.Bookings;

public enum PlayerBookingReviewResultStatus
{
    Success,
    Created,
    BadRequest,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict
}

public sealed record PlayerBookingReviewResult(
    PlayerBookingReviewResultStatus Status,
    PlayerBookingReviewResponse? Value = null,
    string? ErrorMessage = null)
{
    public bool IsSuccess => Status is PlayerBookingReviewResultStatus.Success or PlayerBookingReviewResultStatus.Created;

    public static PlayerBookingReviewResult Success(BookingReviewResponse value) =>
        new(PlayerBookingReviewResultStatus.Success, Value: value as PlayerBookingReviewResponse ?? MapResponse(value));

    public static PlayerBookingReviewResult Created(BookingReviewResponse value) =>
        new(PlayerBookingReviewResultStatus.Created, Value: value as PlayerBookingReviewResponse ?? MapResponse(value));

    private static PlayerBookingReviewResponse MapResponse(BookingReviewResponse r) => new()
    {
        RatingId = r.RatingId,
        BookingId = r.BookingId,
        VenueId = r.VenueId,
        VenueName = r.VenueName,
        Score = r.Score,
        Comment = r.Comment,
        Tags = r.Tags,
        IsAnonymous = r.IsAnonymous,
        CreatedAt = r.CreatedAt
    };

    public static PlayerBookingReviewResult BadRequest(string message) =>
        new(PlayerBookingReviewResultStatus.BadRequest, ErrorMessage: message);

    public static PlayerBookingReviewResult Unauthorized() =>
        new(PlayerBookingReviewResultStatus.Unauthorized, ErrorMessage: "Vui lòng đăng nhập.");

    public static PlayerBookingReviewResult Forbidden(string message) =>
        new(PlayerBookingReviewResultStatus.Forbidden, ErrorMessage: message);

    public static PlayerBookingReviewResult NotFound(string message) =>
        new(PlayerBookingReviewResultStatus.NotFound, ErrorMessage: message);

    public static PlayerBookingReviewResult Conflict(string message) =>
        new(PlayerBookingReviewResultStatus.Conflict, ErrorMessage: message);
}
