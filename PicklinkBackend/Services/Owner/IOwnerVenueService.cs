using PicklinkBackend.DTOs;
using PicklinkBackend.Services.Shared;

namespace PicklinkBackend.Services.Owner;

public interface IOwnerVenueService
{
    void SetCurrentUserId(int? userId);
    Task<ServiceResult<List<OwnerVenueResponse>>> GetVenues(CancellationToken cancellationToken);
    Task<ServiceResult<OwnerVenueResponse>> GetVenue(int venueId, CancellationToken cancellationToken);
    Task<ServiceResult<OwnerVenueResponse>> CreateVenue(OwnerVenueUpsertRequest request, CancellationToken cancellationToken);
    Task<ServiceResult<OwnerVenueResponse>> UpdateVenue(int venueId, OwnerVenueUpsertRequest request, CancellationToken cancellationToken);
    Task<ServiceResult<OwnerVenueResponse>> SetVenueOpenStatus(int venueId, OwnerVenueOpenStatusRequest request, CancellationToken cancellationToken);
    Task<ServiceResult<OwnerVenueResponse>> SubmitVenueForApproval(int venueId, CancellationToken cancellationToken);
    Task<ServiceResult<OwnerListingFeePreviewResponse>> PreviewListingFee(int venueId, int months = 1, CancellationToken cancellationToken = default);
    Task<ServiceResult<OwnerListingFeePaymentResponse>> SubmitListingFeePayment(int venueId, OwnerListingFeePaymentRequest request, CancellationToken cancellationToken);
    Task<ServiceResult<OwnerVenueImageResponse>> UploadVenueImage(int venueId, OwnerVenueImageUploadRequest request, CancellationToken cancellationToken);
    Task<ServiceResult<OwnerVenueResponse>> SetPrimaryVenueImage(int venueId, int imageId, CancellationToken cancellationToken);
    Task<ServiceResult> DeleteVenueImage(int venueId, int imageId, CancellationToken cancellationToken);
    Task<ServiceResult> DeleteVenue(int venueId, CancellationToken cancellationToken);
    Task<ServiceResult<OwnerCourtResponse>> CreateCourt(int venueId, OwnerCourtUpsertRequest request, CancellationToken cancellationToken);
    Task<ServiceResult<OwnerCourtResponse>> UpdateCourt(int courtId, OwnerCourtUpsertRequest request, CancellationToken cancellationToken);
    Task<ServiceResult> DeleteCourt(int courtId, CancellationToken cancellationToken);
    Task<ServiceResult<OwnerScheduleResponse>> GetScheduleV2(DateOnly date, string view = "day", CancellationToken cancellationToken = default);
    Task<ServiceResult<OwnerScheduleResponse>> GetSchedule(DateOnly date, CancellationToken cancellationToken);
    Task<ServiceResult<OwnerScheduleItemResponse>> CreateScheduleEntry(OwnerScheduleBlockRequest request, CancellationToken cancellationToken);
    Task<ServiceResult<OwnerScheduleItemResponse>> CreateBlock(OwnerScheduleBlockRequest request, CancellationToken cancellationToken);
    Task<ServiceResult> DeleteScheduleEntry(int bookingId, CancellationToken cancellationToken);
    Task<ServiceResult> DeleteBlock(int bookingId, CancellationToken cancellationToken);
    Task<ServiceResult> UpdateBookingStatus(int bookingId, OwnerBookingStatusRequest request, CancellationToken cancellationToken);
}
