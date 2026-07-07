using System.Data;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;

namespace PicklinkBackend.Services;

public sealed class AdminVenueApprovalService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly NotificationService _notifications;
    private readonly VenueRealtimeNotifier _venueRealtime;

    public AdminVenueApprovalService(
        ApplicationDbContext dbContext,
        NotificationService notifications,
        VenueRealtimeNotifier venueRealtime)
    {
        _dbContext = dbContext;
        _notifications = notifications;
        _venueRealtime = venueRealtime;
    }

    public async Task<AdminVenueApprovalResult> ApproveAsync(
        int venueId,
        int actorUserId,
        CancellationToken cancellationToken)
    {
        var actor = await _dbContext.Users.SingleOrDefaultAsync(
            user => user.UserId == actorUserId,
            cancellationToken);
        if (actor is null) return AdminVenueApprovalResult.Unauthorized();

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var venue = await LoadVenue(venueId, cancellationToken);
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
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _notifications.PublishPending();
        _venueRealtime.Publish(venueId, "Approved");
        return AdminVenueApprovalResult.Success(AdminVenueQueryService.MapDetail(venue));
    }

    public async Task<AdminVenueApprovalResult> RejectAsync(
        int venueId,
        string? reason,
        int actorUserId,
        CancellationToken cancellationToken)
    {
        var actor = await _dbContext.Users.SingleOrDefaultAsync(
            user => user.UserId == actorUserId,
            cancellationToken);
        if (actor is null) return AdminVenueApprovalResult.Unauthorized();

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var venue = await LoadVenue(venueId, cancellationToken);
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
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _notifications.PublishPending();
        _venueRealtime.Publish(venueId, "Rejected");
        return AdminVenueApprovalResult.Success(AdminVenueQueryService.MapDetail(venue));
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
            .SingleOrDefaultAsync(venue => venue.VenueId == venueId, cancellationToken);
    }
}

public sealed record AdminVenueApprovalResult(
    AdminVenueApprovalResultStatus Status,
    AdminVenueDetailResponse? Venue = null,
    string? ErrorMessage = null)
{
    public static AdminVenueApprovalResult Success(AdminVenueDetailResponse venue) =>
        new(AdminVenueApprovalResultStatus.Success, venue, ErrorMessage: null);

    public static AdminVenueApprovalResult Unauthorized() =>
        new(AdminVenueApprovalResultStatus.Unauthorized, Venue: null, ErrorMessage: null);

    public static AdminVenueApprovalResult NotFound(string errorMessage) =>
        new(AdminVenueApprovalResultStatus.NotFound, Venue: null, ErrorMessage: errorMessage);

    public static AdminVenueApprovalResult BadRequest(string errorMessage) =>
        new(AdminVenueApprovalResultStatus.BadRequest, Venue: null, ErrorMessage: errorMessage);

    public static AdminVenueApprovalResult Conflict(string errorMessage) =>
        new(AdminVenueApprovalResultStatus.Conflict, Venue: null, ErrorMessage: errorMessage);
}

public enum AdminVenueApprovalResultStatus
{
    Success,
    Unauthorized,
    NotFound,
    BadRequest,
    Conflict
}
