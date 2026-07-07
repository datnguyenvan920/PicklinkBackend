using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/bookings")]
public class AdminBookingsController : ControllerBase
{
    private readonly AdminBookingQueryService _bookings;

    public AdminBookingsController(AdminBookingQueryService bookings)
    {
        _bookings = bookings;
    }

    [HttpGet]
    public async Task<ActionResult<PaginatedResponse<AdminBookingSummaryResponse>>> GetBookings(
        string? search,
        string? status,
        string? paymentStatus,
        int page = Pagination.DefaultPage,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        return Ok(await _bookings.ListAsync(
            search,
            status,
            paymentStatus,
            page,
            pageSize,
            cancellationToken));
    }
}