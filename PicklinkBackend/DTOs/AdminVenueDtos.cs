namespace PicklinkBackend.DTOs;

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
