using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services.Ticketing;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize(Roles = "VenueOwner")]
[Route("api/owner/ticket-sessions")]
public sealed class OwnerTicketSessionsController : TicketingControllerBase
{
    private readonly ITicketingService _ticketing;

    public OwnerTicketSessionsController(ITicketingService ticketing)
    {
        _ticketing = ticketing;
    }

    [HttpGet]
    public async Task<ActionResult<PaginatedResponse<TicketSessionResponse>>> GetSessions(
        string? status,
        int? venueId,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        string? search,
        string? playFormat,
        int page = 1,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await _ticketing.GetOwnerSessions(
            CurrentUserId(), status, venueId, dateFrom, dateTo, search, playFormat, page, pageSize, cancellationToken));

    [HttpPost]
    public async Task<ActionResult<TicketSessionResponse>> CreateSession(
        CreateTicketSessionRequest request,
        CancellationToken cancellationToken) =>
        ToActionResult(await _ticketing.CreateSession(CurrentUserId(), request, cancellationToken));

    [HttpPut("{ticketSessionId:int}")]
    public async Task<ActionResult<TicketSessionResponse>> UpdateSession(
        int ticketSessionId,
        UpdateTicketSessionRequest request,
        CancellationToken cancellationToken) =>
        ToActionResult(await _ticketing.UpdateSession(
            CurrentUserId(), ticketSessionId, request, cancellationToken));

    [HttpPost("{ticketSessionId:int}/publish")]
    public async Task<ActionResult<TicketSessionResponse>> PublishSession(
        int ticketSessionId,
        CancellationToken cancellationToken) =>
        ToActionResult(await _ticketing.PublishSession(
            CurrentUserId(), ticketSessionId, cancellationToken));

    [HttpPost("{ticketSessionId:int}/cancel")]
    public async Task<ActionResult<TicketSessionResponse>> CancelSession(
        int ticketSessionId,
        CancelTicketSessionRequest request,
        CancellationToken cancellationToken) =>
        ToActionResult(await _ticketing.CancelSession(
            CurrentUserId(), ticketSessionId, request, cancellationToken));

    [HttpGet("{ticketSessionId:int}/participants")]
    public async Task<ActionResult<TicketSessionParticipantsResponse>> GetParticipants(
        int ticketSessionId,
        CancellationToken cancellationToken) =>
        ToActionResult(await _ticketing.GetOwnerParticipants(
            CurrentUserId(), ticketSessionId, cancellationToken));

    [HttpPost("{ticketSessionId:int}/tickets/check-in")]
    public async Task<ActionResult<SessionTicketResponse>> CheckInTicket(
        int ticketSessionId,
        CheckInSessionTicketRequest request,
        CancellationToken cancellationToken) =>
        ToActionResult(await _ticketing.CheckInOwnerTicket(
            CurrentUserId(), ticketSessionId, request, cancellationToken));

    [HttpPost("~/api/owner/tickets/check-in")]
    public async Task<ActionResult<SessionTicketResponse>> CheckInTicketByCode(
        CheckInSessionTicketRequest request,
        CancellationToken cancellationToken) =>
        ToActionResult(await _ticketing.CheckInOwnerTicketByCode(
            CurrentUserId(), request, cancellationToken));

    [HttpPost("{ticketSessionId:int}/tickets/{sessionTicketId:int}/refund")]
    public async Task<ActionResult<SessionTicketResponse>> RefundTicket(
        int ticketSessionId,
        int sessionTicketId,
        CancelSessionTicketRequest request,
        CancellationToken cancellationToken) =>
        ToActionResult(await _ticketing.RefundOwnerTicket(
            CurrentUserId(), ticketSessionId, sessionTicketId, request, cancellationToken));

    [HttpGet("check-in/today")]
    public async Task<ActionResult<PaginatedResponse<SessionTicketResponse>>> GetCheckInTickets(
        DateOnly date,
        int? venueId,
        int page = 1,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await _ticketing.GetOwnerCheckInTickets(
            CurrentUserId(), date, venueId, page, pageSize, cancellationToken));
}
