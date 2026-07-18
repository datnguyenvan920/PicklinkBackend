using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services.Ticketing;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize(Roles = "Staff")]
[Route("api/staff/ticket-sessions")]
public sealed class StaffTicketSessionsController : TicketingControllerBase
{
    private readonly TicketingService _ticketing;

    public StaffTicketSessionsController(TicketingService ticketing)
    {
        _ticketing = ticketing;
    }

    [HttpGet]
    public async Task<ActionResult<PaginatedResponse<TicketSessionResponse>>> GetSessions(
        DateOnly date,
        int page = 1,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await _ticketing.GetStaffSessions(
            CurrentUserId(), date, page, pageSize, cancellationToken));

    [HttpGet("{ticketSessionId:int}/participants")]
    public async Task<ActionResult<StaffTicketSessionParticipantsResponse>> GetParticipants(
        int ticketSessionId,
        CancellationToken cancellationToken) =>
        ToActionResult(await _ticketing.GetStaffParticipants(
            CurrentUserId(), ticketSessionId, cancellationToken));

    [HttpPost("~/api/staff/tickets/check-in")]
    public async Task<ActionResult<StaffTicketParticipantResponse>> CheckInTicket(
        CheckInSessionTicketRequest request,
        CancellationToken cancellationToken) =>
        ToActionResult(await _ticketing.CheckInTicket(
            CurrentUserId(), request, cancellationToken));
}
