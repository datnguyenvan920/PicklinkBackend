using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;

namespace PicklinkBackend.Services;

public sealed class AdminVenueQueryService
{
    private static readonly string[] ApprovalStatuses = ["Draft", "Pending", "Approved", "Rejected"];
    private readonly ApplicationDbContext _dbContext;

    public AdminVenueQueryService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<AdminVenueListResult> ListAsync(
        string? search,
        string? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);
        var keyword = search?.Trim();
        var normalizedStatus = NormalizeStatus(status);
        if (!string.IsNullOrWhiteSpace(status)
            && !status.Equals("all", StringComparison.OrdinalIgnoreCase)
            && normalizedStatus is null)
        {
            return AdminVenueListResult.InvalidStatus("Trạng thái duyệt sân không hợp lệ.");
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

        return AdminVenueListResult.Success(Pagination.Create(items, totalCount, page, pageSize));
    }

    public async Task<AdminVenueDetailResponse?> GetDetailAsync(
        int venueId,
        CancellationToken cancellationToken)
    {
        var venue = await LoadVenue(venueId, cancellationToken);
        return venue is null ? null : MapDetail(venue);
    }

    internal static AdminVenueDetailResponse MapDetail(Venue venue)
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

    private async Task<Venue?> LoadVenue(
        int venueId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Venues
            .Include(venue => venue.Owner).ThenInclude(owner => owner.User)
            .Include(venue => venue.Amenities)
            .Include(venue => venue.BookingRules)
            .Include(venue => venue.VenueImages)
            .Include(venue => venue.Courts)
            .Include(venue => venue.VenueAuditLogs).ThenInclude(log => log.Actor)
            .AsSplitQuery()
            .AsNoTracking()
            .SingleOrDefaultAsync(venue => venue.VenueId == venueId, cancellationToken);
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
}

public sealed record AdminVenueListResult(
    PaginatedResponse<AdminVenueSummaryResponse>? Venues,
    string? ErrorMessage)
{
    public bool IsInvalidStatus => ErrorMessage is not null;

    public static AdminVenueListResult Success(PaginatedResponse<AdminVenueSummaryResponse> venues) =>
        new(venues, ErrorMessage: null);

    public static AdminVenueListResult InvalidStatus(string errorMessage) =>
        new(Venues: null, ErrorMessage: errorMessage);
}
