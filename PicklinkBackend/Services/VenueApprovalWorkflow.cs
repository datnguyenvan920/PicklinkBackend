using PicklinkBackend.Models;

namespace PicklinkBackend.Services;

public static class VenueApprovalWorkflow
{
    public static string? Approve(Venue venue, User actor, DateTime now)
    {
        if (!string.Equals(venue.ApprovalStatus, "Pending", StringComparison.OrdinalIgnoreCase))
            return "Chỉ có thể duyệt sân đang chờ duyệt.";

        venue.ApprovalStatus = "Approved";
        venue.RejectionReason = null;
        AddAuditLog(venue, actor, "AdminApprovedVenue", now);
        return null;
    }

    public static string? Reject(Venue venue, User actor, string? reason, DateTime now)
    {
        if (!string.Equals(venue.ApprovalStatus, "Pending", StringComparison.OrdinalIgnoreCase))
            return "Chỉ có thể từ chối sân đang chờ duyệt.";

        var normalizedReason = reason?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedReason) || normalizedReason.Length < 3)
            return "Lý do từ chối phải có ít nhất 3 ký tự.";
        if (normalizedReason.Length > 500)
            return "Lý do từ chối không được vượt quá 500 ký tự.";

        venue.ApprovalStatus = "Rejected";
        venue.RejectionReason = normalizedReason;
        AddAuditLog(venue, actor, "AdminRejectedVenue", now);
        return null;
    }

    private static void AddAuditLog(Venue venue, User actor, string action, DateTime now)
    {
        venue.VenueAuditLogs.Add(new VenueAuditLog
        {
            VenueId = venue.VenueId,
            ActorId = actor.UserId,
            Actor = actor,
            Action = action,
            Timestamp = now
        });
    }
}
