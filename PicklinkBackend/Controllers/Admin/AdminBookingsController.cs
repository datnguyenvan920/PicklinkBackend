using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services.Admin;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/bookings")]
public class AdminBookingsController : ControllerBase
{
    private readonly IAdminBookingService _bookingService;

    public AdminBookingsController(IAdminBookingService bookingService)
    {
        _bookingService = bookingService;
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
        return Ok(await _bookingService.ListAsync(
            search,
            status,
            paymentStatus,
            page,
            pageSize,
            cancellationToken));
    }
}
