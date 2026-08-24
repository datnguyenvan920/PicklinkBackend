using PicklinkBackend.DTOs;
using PicklinkBackend.Services.Shared;

namespace PicklinkBackend.Services.Ticketing;

public interface ITicketingService
{
    Task<ServiceResult<PaginatedResponse<TicketSessionResponse>>> GetPublishedSessions(
        string? search,
        int? venueId,
        DateOnly? date,
        int? skillLevel,
        string? playFormat,
        decimal? minPrice,
        decimal? maxPrice,
        bool onlyAvailable,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<ServiceResult<TicketSessionResponse>> GetPublishedSession(int ticketSessionId, int? userId, CancellationToken cancellationToken);

    Task<ServiceResult<PaginatedResponse<TicketSessionResponse>>> GetOwnerSessions(
        int? userId,
        string? status,
        int? venueId,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        string? search,
        string? playFormat,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<ServiceResult<TicketSessionResponse>> CreateSession(int? userId, CreateTicketSessionRequest request, CancellationToken cancellationToken);
    Task<ServiceResult<TicketSessionResponse>> UpdateSession(int? userId, int ticketSessionId, UpdateTicketSessionRequest request, CancellationToken cancellationToken);
    Task<ServiceResult<TicketSessionResponse>> PublishSession(int? userId, int ticketSessionId, CancellationToken cancellationToken);
    Task<ServiceResult<TicketSessionResponse>> CancelSession(int? userId, int ticketSessionId, CancelTicketSessionRequest request, CancellationToken cancellationToken);
    Task<ServiceResult<TicketSessionParticipantsResponse>> GetOwnerParticipants(int? userId, int ticketSessionId, CancellationToken cancellationToken);
    Task<ServiceResult<SessionTicketResponse>> RefundOwnerTicket(int? userId, int ticketSessionId, int sessionTicketId, CancelSessionTicketRequest request, CancellationToken cancellationToken);

    // Purchase
    Task<ServiceResult<SessionTicketResponse>> PurchaseTicket(int? userId, int ticketSessionId, CancellationToken cancellationToken);

    // Player Tickets
    Task<ServiceResult<PaginatedResponse<SessionTicketResponse>>> GetMyTickets(int? userId, string? status, int page, int pageSize, CancellationToken cancellationToken);
    Task<ServiceResult<SessionTicketResponse>> GetMyTicket(int? userId, int sessionTicketId, CancellationToken cancellationToken);
    Task<ServiceResult<SessionTicketResponse>> CancelMyTicket(int? userId, int sessionTicketId, CancelSessionTicketRequest request, CancellationToken cancellationToken);

    // Staff & CheckIn
    Task<ServiceResult<PaginatedResponse<TicketSessionResponse>>> GetStaffSessions(int? userId, DateOnly date, int page, int pageSize, CancellationToken cancellationToken);
    Task<ServiceResult<StaffTicketSessionParticipantsResponse>> GetStaffParticipants(int? userId, int ticketSessionId, CancellationToken cancellationToken);
    Task<ServiceResult<StaffTicketParticipantResponse>> CheckInTicket(int? userId, CheckInSessionTicketRequest request, CancellationToken cancellationToken);
    Task<ServiceResult<SessionTicketResponse>> CheckInOwnerTicket(int? userId, int ticketSessionId, CheckInSessionTicketRequest request, CancellationToken cancellationToken);
    Task<ServiceResult<SessionTicketResponse>> CheckInOwnerTicketByCode(int? userId, CheckInSessionTicketRequest request, CancellationToken cancellationToken);
    Task<ServiceResult<PaginatedResponse<SessionTicketResponse>>> GetOwnerCheckInTickets(int? userId, DateOnly date, int? venueId, int page, int pageSize, CancellationToken cancellationToken);
}
