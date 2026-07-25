using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services.Admin;
using PicklinkBackend.Services.Admin.Implementations;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/bookings")]
public class AdminBookingsController : ControllerBase
{
    private readonly IAdminVenueService _venueService;

    public AdminBookingsController(IAdminVenueService venueService)
    {
        _venueService = venueService;
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
        return Ok(await _venueService.ListBookingsAsync(
            search,
            status,
            paymentStatus,
            page,
            pageSize,
            cancellationToken));
    }
}