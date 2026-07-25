using System.Data;
using System.Globalization;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Repositories;
using PicklinkBackend.Services.Admin;
using PicklinkBackend.Services.Notifications;
using PicklinkBackend.Services.Notifications.Implementations;
using PicklinkBackend.Services.Venues;

namespace PicklinkBackend.Services.Admin.Implementations;

public sealed class AdminVenueService : IAdminVenueService
{
    private static readonly string[] ApprovalStatuses = ["Draft", "Pending", "Approved", "Rejected"];
    private readonly IAdminRepository _adminRepository;
    private readonly IUserRepository _userRepository;
    private readonly NotificationService _notifications;
    private readonly VenueRealtimeNotifier _venueRealtime;

    public AdminVenueService(
        IAdminRepository adminRepository,
        IUserRepository userRepository,
        NotificationService notifications,
        VenueRealtimeNotifier venueRealtime)
    {
        _adminRepository = adminRepository;
        _userRepository = userRepository;
        _notifications = notifications;
        _venueRealtime = venueRealtime;
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

        var (items, totalCount) = await _adminRepository.GetAdminVenueListAsync(
            keyword, normalizedStatus, page, pageSize, cancellationToken);

        return AdminVenueListResult.Success(Pagination.Create(items.Cast<AdminVenueResponse>().ToList(), totalCount, page, pageSize));
    }

    public async Task<AdminVenueDetailResponse?> GetDetailAsync(
        int venueId,
        CancellationToken cancellationToken)
    {
        var venue = await _adminRepository.GetAdminVenueDetailAsync(venueId, cancellationToken);
        return venue is null ? null : MapDetail(venue);
    }

    public async Task<AdminVenueApprovalResult> ApproveAsync(
        int venueId,
        int actorUserId,
        CancellationToken cancellationToken)
    {
        var actor = await _userRepository.GetByIdAsync(actorUserId, cancellationToken);
        if (actor is null) return AdminVenueApprovalResult.Unauthorized();

        await using var transaction = await _adminRepository.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var venue = await _adminRepository.GetVenueForApprovalByIdAsync(venueId, cancellationToken);
        if (venue is null)
            return AdminVenueApprovalResult.NotFound("Không tìm thấy cụm sân.");

        var error = VenueApprovalWorkflow.Approve(venue, actor, DateTime.UtcNow);
        if (error is not null) return AdminVenueApprovalResult.Conflict(error);

        _notifications.Add(new NotificationInput(
            UserId: venue.Owner.UserId,
            Type: NotificationTypes.Court,
            Title: "Sân đã được duyệt",
            Message: $"Cụm sân \"{venue.VenueName}\" đã được Admin duyệt.",
            Tone: NotificationTones.Success,
            LinkTo: $"/owner/venues/{venue.VenueId}",
            LinkLabel: "Xem sân"));
        await _adminRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _notifications.PublishPending();
        _venueRealtime.Publish(venueId, "Approved");
        return AdminVenueApprovalResult.Success(MapDetail(venue));
    }

    public async Task<AdminVenueApprovalResult> RejectAsync(
        int venueId,
        string? reason,
        int actorUserId,
        CancellationToken cancellationToken)
    {
        var actor = await _userRepository.GetByIdAsync(actorUserId, cancellationToken);
        if (actor is null) return AdminVenueApprovalResult.Unauthorized();

        await using var transaction = await _adminRepository.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var venue = await _adminRepository.GetVenueForApprovalByIdAsync(venueId, cancellationToken);
        if (venue is null)
            return AdminVenueApprovalResult.NotFound("Không tìm thấy cụm sân.");

        var error = VenueApprovalWorkflow.Reject(
            venue,
            actor,
            reason,
            DateTime.UtcNow);
        if (error is not null)
        {
            return string.Equals(venue.ApprovalStatus, "Pending", StringComparison.OrdinalIgnoreCase)
                ? AdminVenueApprovalResult.BadRequest(error)
                : AdminVenueApprovalResult.Conflict(error);
        }

        _notifications.Add(new NotificationInput(
            UserId: venue.Owner.UserId,
            Type: NotificationTypes.Court,
            Title: "Sân bị từ chối",
            Message: $"Cụm sân \"{venue.VenueName}\" bị từ chối: {venue.RejectionReason}",
            Tone: NotificationTones.Urgent,
            LinkTo: $"/owner/venues/{venue.VenueId}",
            LinkLabel: "Chỉnh sửa sân"));
        await _adminRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _notifications.PublishPending();
        _venueRealtime.Publish(venueId, "Rejected");
        return AdminVenueApprovalResult.Success(MapDetail(venue));
    }

    public async Task<PaginatedResponse<AdminBookingSummaryResponse>> ListBookingsAsync(
        string? search,
        string? status,
        string? paymentStatus,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);
        var keyword = search?.Trim();
        var normalizedStatus = Normalize(status);
        var normalizedPaymentStatus = Normalize(paymentStatus);

        var (items, totalCount) = await _adminRepository.GetAdminBookingListAsync(
            keyword, normalizedStatus, normalizedPaymentStatus, page, pageSize, cancellationToken);

        return Pagination.Create(items, totalCount, page, pageSize);
    }

    public static AdminVenueDetailResponse MapDetail(Venue venue)
    {
        var submittedAt = venue.VenueAuditLogs
            .Where(log => log.Action == "OwnerSubmittedForApproval")
            .OrderByDescending(log => log.Timestamp)
            .Select(log => (DateTime?)log.Timestamp)
            .FirstOrDefault();
        var basePrice = decimal.TryParse(
            venue.BookingRules.FirstOrDefault(rule => rule.RuleType == "BasePrice")?.RuleContent,
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var parsedBasePrice)
            ? parsedBasePrice
            : 0m;

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
            OpenTime = venue.OpenTime.ToString("hh\\:mm"),
            CloseTime = venue.CloseTime.ToString("hh\\:mm"),
            Latitude = venue.Latitude,
            Longitude = venue.Longitude,
            BasePrice = basePrice,
            Amenities = venue.Amenities.Select(a => a.AmenityName).ToList(),
            Images = venue.VenueImages
                .OrderByDescending(i => i.IsPrimary)
                .ThenBy(i => i.SortOrder)
                .Select(i => new AdminVenueImageResponse
                {
                    VenueImageId = i.VenueImageId,
                    ImageUrl = i.ImageUrl,
                    Caption = i.Caption,
                    IsPrimary = i.IsPrimary
                })
                .ToList(),
            Courts = venue.Courts
                .OrderBy(c => c.CourtNumber)
                .Select(c => new AdminVenueCourtResponse
                {
                    CourtId = c.CourtId,
                    CourtNumber = c.CourtNumber,
                    CourtType = c.CourtType,
                    SurfaceType = c.SurfaceType,
                    HourlyPrice = c.HourlyPrice,
                    IsIndoor = c.IsIndoor,
                    AvailabilityStatus = c.AvailabilityStatus
                })
                .ToList(),
            AuditLogs = venue.VenueAuditLogs
                .OrderByDescending(l => l.Timestamp)
                .Select(l => new AdminVenueAuditResponse
                {
                    VenueAuditLogId = l.LogId,
                    ActorId = l.ActorId,
                    ActorName = l.Actor.Username,
                    Action = l.Action,
                    Timestamp = l.Timestamp
                })
                .ToList()
        };
    }

    private static string? NormalizeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status) || status.Equals("all", StringComparison.OrdinalIgnoreCase))
            return null;

        return ApprovalStatuses.FirstOrDefault(item => item.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? null
            : value.Trim();
}
