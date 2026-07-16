namespace PicklinkBackend.DTOs;

public sealed class AdminDashboardResponse
{
    public int TotalUsers { get; set; }
    public int LockedUserCount { get; set; }
    public int ActiveVenueCount { get; set; }
    public int PendingVenueCount { get; set; }
    public int TotalCourtCount { get; set; }
    public int TodayBookingCount { get; set; }
    public decimal TodayBookingRevenue { get; set; }
    public int PendingBookingPaymentCount { get; set; }
    public int PendingListingPaymentCount { get; set; }
    public decimal ListingRevenueThisMonth { get; set; }
    public int ExpiringListingCount { get; set; }
    public int ExpiredListingCount { get; set; }
    public List<AdminDashboardActionItemResponse> ActionItems { get; set; } = [];
    public List<AdminDashboardExpiringListingResponse> ExpiringListings { get; set; } = [];
}

public sealed class AdminDashboardActionItemResponse
{
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Tone { get; set; } = "neutral";
    public string LinkTo { get; set; } = string.Empty;
    public DateTime? CreatedAt { get; set; }
}

public sealed class AdminDashboardExpiringListingResponse
{
    public int VenueId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;
    public int CourtCount { get; set; }
    public DateTime? PaidUntil { get; set; }
}