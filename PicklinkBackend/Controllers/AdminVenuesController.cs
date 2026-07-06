using System.Data;
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Services;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/venues")]
public class AdminVenuesController : ControllerBase
{
    private static readonly string[] ApprovalStatuses = ["Draft", "Pending", "Approved", "Rejected"];
    private readonly ApplicationDbContext _dbContext;
    private readonly VenueRealtimeNotifier _venueRealtime;

    public AdminVenuesController(
        ApplicationDbContext dbContext,
        VenueRealtimeNotifier venueRealtime)
    {
        _dbContext = dbContext;
        _venueRealtime = venueRealtime;
    }

    [HttpGet]
    public async Task<ActionResult<PaginatedResponse<AdminVenueSummaryResponse>>> GetVenues(
        string? search,
        string? status,
        int page = Pagination.DefaultPage,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);
        var keyword = search?.Trim();
        var normalizedStatus = NormalizeStatus(status);
        if (!string.IsNullOrWhiteSpace(status)
            && !status.Equals("all", StringComparison.OrdinalIgnoreCase)
            && normalizedStatus is null)
        {
            return BadRequest(new { message = "Trạng thái duyệt sân không hợp lệ." });
        }

        var query = _dbContext.Venues.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(venue =>
                venue.VenueName.Contains(keyword)
                || venue.Address.Contains(keyword)
                || venue.Owner.User.Username.Contains(keyword)
                || venue.Owner.User.Email.Contains(keyword));
        }
        if (normalizedStatus is not null)
            query = query.Where(venue => venue.ApprovalStatus == normalizedStatus);

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(venue => venue.ApprovalStatus == "Pending")
            .ThenByDescending(venue => venue.VenueAuditLogs
                .Where(log => log.Action == "OwnerSubmittedForApproval")
                .Select(log => (DateTime?)log.Timestamp)
                .Max())
            .ThenBy(venue => venue.VenueName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(venue => new AdminVenueSummaryResponse
            {
                VenueId = venue.VenueId,
                VenueName = venue.VenueName,
                Address = venue.Address,
                OwnerUserId = venue.Owner.UserId,
                OwnerName = venue.Owner.User.Username,
                OwnerEmail = venue.Owner.User.Email,
                PhoneNumber = venue.PhoneNumber,
                OverallRating = venue.OverallRating,
                IsOpen = venue.IsOpen,
                ApprovalStatus = venue.ApprovalStatus,
                RejectionReason = venue.RejectionReason,
                CourtCount = venue.Courts.Count,
                PrimaryImageUrl = venue.VenueImages
                    .OrderByDescending(image => image.IsPrimary)
                    .ThenBy(image => image.SortOrder)
                    .Select(image => image.ImageUrl)
                    .FirstOrDefault(),
                SubmittedAt = venue.VenueAuditLogs
                    .Where(log => log.Action == "OwnerSubmittedForApproval")
                    .Select(log => (DateTime?)log.Timestamp)
                    .Max()
            })
            .ToListAsync(cancellationToken);

        return Ok(Pagination.Create(items, totalCount, page, pageSize));
    }

    [HttpGet("{venueId:int}")]
    public async Task<ActionResult<AdminVenueDetailResponse>> GetVenue(
        int venueId,
        CancellationToken cancellationToken)
    {
        var venue = await LoadVenue(venueId, asTracking: false, cancellationToken);
        return venue is null
            ? NotFound(new { message = "Không tìm thấy cụm sân." })
            : Ok(MapDetail(venue));
    }

    [HttpPost("{venueId:int}/approve")]
    public async Task<ActionResult<AdminVenueDetailResponse>> ApproveVenue(
        int venueId,
        CancellationToken cancellationToken)
    {
        var actorId = CurrentUserId();
        if (actorId is null) return Unauthorized();
        var actor = await _dbContext.Users.SingleOrDefaultAsync(
            user => user.UserId == actorId.Value,
            cancellationToken);
        if (actor is null) return Unauthorized();

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var venue = await LoadVenue(venueId, asTracking: true, cancellationToken);
        if (venue is null) return NotFound(new { message = "Không tìm thấy cụm sân." });

        var error = VenueApprovalWorkflow.Approve(venue, actor, DateTime.UtcNow);
        if (error is not null) return Conflict(new { message = error });

        _dbContext.NotificationLogs.Add(new NotificationLog
        {
            UserId = venue.Owner.UserId,
            Message = $"Cụm sân \"{venue.VenueName}\" đã được Admin duyệt.",
            IsRead = false
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _venueRealtime.Publish(venueId, "Approved");
        return Ok(MapDetail(venue));
    }

    [HttpPost("{venueId:int}/reject")]
    public async Task<ActionResult<AdminVenueDetailResponse>> RejectVenue(
        int venueId,
        AdminVenueRejectionRequest request,
        CancellationToken cancellationToken)
    {
        var actorId = CurrentUserId();
        if (actorId is null) return Unauthorized();
        var actor = await _dbContext.Users.SingleOrDefaultAsync(
            user => user.UserId == actorId.Value,
            cancellationToken);
        if (actor is null) return Unauthorized();

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var venue = await LoadVenue(venueId, asTracking: true, cancellationToken);
        if (venue is null) return NotFound(new { message = "Không tìm thấy cụm sân." });

        var error = VenueApprovalWorkflow.Reject(
            venue,
            actor,
            request.Reason,
            DateTime.UtcNow);
        if (error is not null)
        {
            return string.Equals(venue.ApprovalStatus, "Pending", StringComparison.OrdinalIgnoreCase)
                ? BadRequest(new { message = error })
                : Conflict(new { message = error });
        }

        _dbContext.NotificationLogs.Add(new NotificationLog
        {
            UserId = venue.Owner.UserId,
            Message = $"Cụm sân \"{venue.VenueName}\" bị từ chối: {venue.RejectionReason}",
            IsRead = false
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _venueRealtime.Publish(venueId, "Rejected");
        return Ok(MapDetail(venue));
    }

    private int? CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : null;

    private async Task<Venue?> LoadVenue(
        int venueId,
        bool asTracking,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.Venues
            .Include(venue => venue.Owner).ThenInclude(owner => owner.User)
            .Include(venue => venue.Amenities)
            .Include(venue => venue.BookingRules)
            .Include(venue => venue.VenueImages)
            .Include(venue => venue.Courts)
            .Include(venue => venue.VenueAuditLogs).ThenInclude(log => log.Actor)
            .AsSplitQuery();
        if (!asTracking) query = query.AsNoTracking();
        return await query.SingleOrDefaultAsync(
            venue => venue.VenueId == venueId,
            cancellationToken);
    }

    private static string? NormalizeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)
            || status.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return ApprovalStatuses.FirstOrDefault(item =>
            item.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static AdminVenueDetailResponse MapDetail(Venue venue)
    {
        var submittedAt = venue.VenueAuditLogs
            .Where(log => log.Action == "OwnerSubmittedForApproval")
            .OrderByDescending(log => log.Timestamp)
            .Select(log => (DateTime?)log.Timestamp)
            .FirstOrDefault();
        var basePrice = double.TryParse(
            venue.BookingRules.FirstOrDefault(rule => rule.RuleType == "BasePrice")?.RuleContent,
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var parsedPrice)
            ? parsedPrice
            : 0;

        return new AdminVenueDetailResponse
        {
            VenueId = venue.VenueId,
            VenueName = venue.VenueName,
            Address = venue.Address,
            OwnerUserId = venue.Owner.UserId,
            OwnerName = venue.Owner.User.Username,
            OwnerEmail = venue.Owner.User.Email,
            PhoneNumber = venue.PhoneNumber,
            OverallRating = venue.OverallRating,
            IsOpen = venue.IsOpen,
            ApprovalStatus = venue.ApprovalStatus,
            RejectionReason = venue.RejectionReason,
            CourtCount = venue.Courts.Count,
            PrimaryImageUrl = venue.VenueImages
                .OrderByDescending(image => image.IsPrimary)
                .ThenBy(image => image.SortOrder)
                .Select(image => image.ImageUrl)
                .FirstOrDefault(),
            SubmittedAt = submittedAt,
            OpenTime = venue.OpenTime.ToString("HH:mm"),
            CloseTime = venue.CloseTime.ToString("HH:mm"),
            Latitude = venue.Latitude,
            Longitude = venue.Longitude,
            BasePrice = basePrice,
            Amenities = venue.Amenities
                .OrderBy(amenity => amenity.AmenityName)
                .Select(amenity => amenity.AmenityName)
                .ToList(),
            Images = venue.VenueImages
                .OrderByDescending(image => image.IsPrimary)
                .ThenBy(image => image.SortOrder)
                .Select(image => new AdminVenueImageResponse
                {
                    VenueImageId = image.VenueImageId,
                    ImageUrl = image.ImageUrl,
                    Caption = image.Caption,
                    IsPrimary = image.IsPrimary
                })
                .ToList(),
            Courts = venue.Courts
                .OrderBy(court => court.CourtNumber)
                .Select(court => new AdminVenueCourtResponse
                {
                    CourtId = court.CourtId,
                    CourtNumber = court.CourtNumber,
                    CourtType = court.CourtType ?? "Standard",
                    SurfaceType = court.SurfaceType,
                    HourlyPrice = court.HourlyPrice,
                    IsIndoor = court.IsIndoor,
                    AvailabilityStatus = court.AvailabilityStatus
                })
                .ToList(),
            AuditLogs = venue.VenueAuditLogs
                .OrderByDescending(log => log.Timestamp)
                .Select(log => new AdminVenueAuditResponse
                {
                    Action = log.Action,
                    ActorName = log.Actor.Username,
                    Timestamp = log.Timestamp
                })
                .ToList()
        };
    }
}

public sealed class AdminVenueRejectionRequest
{
    public string? Reason { get; set; }
}

public class AdminVenueSummaryResponse
{
    public int VenueId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int OwnerUserId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public double OverallRating { get; set; }
    public bool IsOpen { get; set; }
    public string ApprovalStatus { get; set; } = string.Empty;
    public string? RejectionReason { get; set; }
    public int CourtCount { get; set; }
    public string? PrimaryImageUrl { get; set; }
    public DateTime? SubmittedAt { get; set; }
}

public sealed class AdminVenueDetailResponse : AdminVenueSummaryResponse
{
    public string OpenTime { get; set; } = string.Empty;
    public string CloseTime { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double BasePrice { get; set; }
    public List<string> Amenities { get; set; } = [];
    public List<AdminVenueImageResponse> Images { get; set; } = [];
    public List<AdminVenueCourtResponse> Courts { get; set; } = [];
    public List<AdminVenueAuditResponse> AuditLogs { get; set; } = [];
}

public sealed class AdminVenueImageResponse
{
    public int VenueImageId { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string? Caption { get; set; }
    public bool IsPrimary { get; set; }
}

public sealed class AdminVenueCourtResponse
{
    public int CourtId { get; set; }
    public int CourtNumber { get; set; }
    public string CourtType { get; set; } = string.Empty;
    public string? SurfaceType { get; set; }
    public double HourlyPrice { get; set; }
    public bool IsIndoor { get; set; }
    public string AvailabilityStatus { get; set; } = string.Empty;
}

public sealed class AdminVenueAuditResponse
{
    public string Action { get; set; } = string.Empty;
    public string ActorName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
