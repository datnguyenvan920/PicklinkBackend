using PicklinkBackend.Models;

namespace PicklinkBackend.Services.Venues;

public static class VenueApprovalWorkflow
{
    public static string? Approve(Venue venue, User actor, DateTime now)
    {
        if (!string.Equals(venue.ApprovalStatus, "Pending", StringComparison.OrdinalIgnoreCase))
            return "ChÃ¡Â»â€° cÃƒÂ³ thÃ¡Â»Æ’ duyÃ¡Â»â€¡t sÃƒÂ¢n Ã„â€˜ang chÃ¡Â»Â duyÃ¡Â»â€¡t.";

        venue.ApprovalStatus = "Approved";
        venue.RejectionReason = null;
        AddAuditLog(venue, actor, "AdminApprovedVenue", now);
        return null;
    }

    public static string? Reject(Venue venue, User actor, string? reason, DateTime now)
    {
        if (!string.Equals(venue.ApprovalStatus, "Pending", StringComparison.OrdinalIgnoreCase))
            return "ChÃ¡Â»â€° cÃƒÂ³ thÃ¡Â»Æ’ tÃ¡Â»Â« chÃ¡Â»â€˜i sÃƒÂ¢n Ã„â€˜ang chÃ¡Â»Â duyÃ¡Â»â€¡t.";

        var normalizedReason = reason?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedReason) || normalizedReason.Length < 3)
            return "LÃƒÂ½ do tÃ¡Â»Â« chÃ¡Â»â€˜i phÃ¡ÂºÂ£i cÃƒÂ³ ÃƒÂ­t nhÃ¡ÂºÂ¥t 3 kÃƒÂ½ tÃ¡Â»Â±.";
        if (normalizedReason.Length > 500)
            return "LÃƒÂ½ do tÃ¡Â»Â« chÃ¡Â»â€˜i khÃƒÂ´ng Ã„â€˜Ã†Â°Ã¡Â»Â£c vÃ†Â°Ã¡Â»Â£t quÃƒÂ¡ 500 kÃƒÂ½ tÃ¡Â»Â±.";

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
