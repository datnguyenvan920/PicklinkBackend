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
    private readonly TicketingService _ticketing;

    public OwnerTicketSessionsController(TicketingService ticketing)
    {
        _ticketing = ticketing;
    }

    [HttpGet]
    public async Task<ActionResult<PaginatedResponse<TicketSessionResponse>>> GetSessions(
        string? status,
        int page = 1,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await _ticketing.GetOwnerSessions(
            CurrentUserId(), status, page, pageSize, cancellationToken));

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

    [HttpPost("{ticketSessionId:int}/tickets/{sessionTicketId:int}/refund")]
    public async Task<ActionResult<SessionTicketResponse>> CompleteRefund(
        int ticketSessionId,
        int sessionTicketId,
        CompleteTicketRefundRequest request,
        CancellationToken cancellationToken) =>
        ToActionResult(await _ticketing.CompleteRefund(
            CurrentUserId(), ticketSessionId, sessionTicketId, request, cancellationToken));

    [HttpPost("{ticketSessionId:int}/tickets/{sessionTicketId:int}/sepay-transactions/{sePayTransactionId:int}/refund")]
    public async Task<ActionResult<SePayTransactionResponse>> CompleteAdditionalRefund(
        int ticketSessionId,
        int sessionTicketId,
        int sePayTransactionId,
        CompleteTicketRefundRequest request,
        CancellationToken cancellationToken) =>
        ToActionResult(await _ticketing.CompleteAdditionalRefund(
            CurrentUserId(),
            ticketSessionId,
            sessionTicketId,
            sePayTransactionId,
            request,
            cancellationToken));
}
