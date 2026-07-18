using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services.Ticketing;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize(Roles = "Player")]
[Route("api/player/tickets")]
public sealed class PlayerTicketsController : TicketingControllerBase
{
    private readonly TicketingService _ticketing;

    public PlayerTicketsController(TicketingService ticketing)
    {
        _ticketing = ticketing;
    }

    [HttpGet]
    public async Task<ActionResult<PaginatedResponse<SessionTicketResponse>>> GetMyTickets(
        string? status,
        int page = 1,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await _ticketing.GetMyTickets(
            CurrentUserId(), status, page, pageSize, cancellationToken));

    [HttpGet("{sessionTicketId:int}")]
    public async Task<ActionResult<SessionTicketResponse>> GetMyTicket(
        int sessionTicketId,
        CancellationToken cancellationToken) =>
        ToActionResult(await _ticketing.GetMyTicket(
            CurrentUserId(), sessionTicketId, cancellationToken));

    [HttpPost("{sessionTicketId:int}/cancel")]
    public async Task<ActionResult<SessionTicketResponse>> CancelMyTicket(
        int sessionTicketId,
        CancelSessionTicketRequest request,
        CancellationToken cancellationToken) =>
        ToActionResult(await _ticketing.CancelMyTicket(
            CurrentUserId(), sessionTicketId, request, cancellationToken));
}
