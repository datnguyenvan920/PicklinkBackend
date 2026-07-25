using PicklinkBackend.DTOs;

namespace PicklinkBackend.Services.Staff;

public interface IStaffOperationService
{
    Task<StaffOperationResult<List<StaffAssignmentResponse>>> ListAssignmentsAsync(int? userId, CancellationToken cancellationToken);
    Task<StaffOperationResult<PaginatedResponse<StaffBookingResponse>>> ListTodayBookingsAsync(int? userId, DateOnly? date, string? bookingType, int? venueId, int page, int pageSize, CancellationToken cancellationToken);
    Task<StaffOperationResult<StaffBookingResponse>> SearchBookingAsync(int? userId, string code, CancellationToken cancellationToken);
    Task<StaffOperationResult<StaffBookingResponse>> GetBookingAsync(int? userId, int bookingId, CancellationToken cancellationToken);
    Task<StaffOperationResult<StaffBookingResponse>> VerifyBookingCodeByCodeAsync(int? userId, VerifyBookingCodeRequest request, CancellationToken cancellationToken);
    Task<StaffOperationResult<StaffBookingResponse>> CheckInGroupAsync(int? userId, int bookingId, int checkInGroupId, CancellationToken cancellationToken);
    Task<StaffOperationResult<StaffBookingResponse>> MarkGroupNoShowAsync(int? userId, int bookingId, int checkInGroupId, CancellationToken cancellationToken);
    Task<StaffOperationResult<StaffBookingResponse>> ConfirmAtCourtPaymentAsync(int? userId, int bookingId, CancellationToken cancellationToken);
    Task<StaffOperationResult<StaffBookingResponse>> CheckInAsync(int? userId, int bookingId, CancellationToken cancellationToken);
    Task<StaffOperationResult<StaffBookingResponse>> MarkNoShowAsync(int? userId, int bookingId, CancellationToken cancellationToken);
    Task<StaffOperationResult<StaffBookingResponse>> CheckInMatchParticipantAsync(int? userId, int bookingId, int playerId, CancellationToken cancellationToken);
    Task<StaffOperationResult<StaffBookingResponse>> MarkMatchParticipantNoShowAsync(int? userId, int bookingId, int playerId, CancellationToken cancellationToken);
    Task<StaffOperationResult<List<StaffNotificationResponse>>> ListNotificationsAsync(int? userId, CancellationToken cancellationToken);
}
