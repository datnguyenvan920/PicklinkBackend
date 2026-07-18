using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services.Ticketing;

namespace PicklinkBackend.Controllers;

[ApiController]
[Route("api/ticket-sessions")]
public sealed class TicketSessionsController : TicketingControllerBase
{
    private readonly TicketingService _ticketing;

    public TicketSessionsController(TicketingService ticketing)
    {
        _ticketing = ticketing;
    }

    [AllowAnonymous]
    [HttpGet]
    public async Task<ActionResult<PaginatedResponse<TicketSessionResponse>>> GetSessions(
        string? search,
        int? venueId,
        DateOnly? date,
        string? skillLevel,
        string? playFormat,
        decimal? minPrice,
        decimal? maxPrice,
        bool onlyAvailable = false,
        int page = 1,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default) =>
        ToActionResult(await _ticketing.GetPublishedSessions(
            search, venueId, date, skillLevel, playFormat, minPrice, maxPrice,
            onlyAvailable, page, pageSize, cancellationToken));

    [AllowAnonymous]
    [HttpGet("{ticketSessionId:int}")]
    public async Task<ActionResult<TicketSessionResponse>> GetSession(
        int ticketSessionId,
        CancellationToken cancellationToken) =>
        ToActionResult(await _ticketing.GetPublishedSession(ticketSessionId, cancellationToken));

    [Authorize(Roles = "Player")]
    [HttpPost("{ticketSessionId:int}/tickets")]
    public async Task<ActionResult<SessionTicketResponse>> PurchaseTicket(
        int ticketSessionId,
        CancellationToken cancellationToken) =>
        ToActionResult(await _ticketing.PurchaseTicket(
            CurrentUserId(), ticketSessionId, cancellationToken));
}
