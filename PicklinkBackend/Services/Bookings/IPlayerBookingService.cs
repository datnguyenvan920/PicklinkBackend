using PicklinkBackend.DTOs;
using PicklinkBackend.Services.Shared;

namespace PicklinkBackend.Services.Bookings;

public interface IPlayerBookingService
{
    void SetCurrentUserId(int? userId);
    Task<ServiceResult<PaginatedResponse<PlayerVenueSummaryResponse>>> GetVenues(
        string? search,
        string? area,
        decimal? minPrice,
        decimal? maxPrice,
        bool favoritesOnly = false,
        int page = 1,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<PaginatedResponse<PlayerVenueSummaryResponse>>> GetFavoriteVenues(
        int page = 1,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<List<PlayerVenueReviewResponse>>> GetVenueReviews(
        int venueId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> AddFavoriteVenue(int venueId, CancellationToken cancellationToken = default);
    Task<ServiceResult> RemoveFavoriteVenue(int venueId, CancellationToken cancellationToken = default);

    Task<ServiceResult<PlayerCourtAvailabilityResponse>> GetAvailability(
        int venueId,
        DateOnly date,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<BookingHoldingResponse>> CreateHolding(
        CreateBookingHoldRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<PaginatedResponse<BookingHoldingResponse>>> GetMyBookings(
        int page = 1,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<BookingHoldingResponse>> GetBooking(int bookingId, CancellationToken cancellationToken = default);
    Task<ServiceResult<BookingHoldingGroupResponse>> GetHoldingGroup(Guid paymentGroupId, CancellationToken cancellationToken = default);

    Task<ServiceResult<BookingHoldingResponse>> CompletePayment(
        int bookingId,
        CompleteBookingPaymentRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> CancelHolding(int bookingId, CancellationToken cancellationToken = default);

    Task<ServiceResult> CancelBooking(
        int bookingId,
        CancelPlayerBookingRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<BookingHoldingResponse>> RetryPayment(int bookingId, CancellationToken cancellationToken = default);
}
