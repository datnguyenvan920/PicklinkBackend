using System.ComponentModel.DataAnnotations;

namespace PicklinkBackend.DTOs;

public class OwnerVenueUpsertRequest
{
    [Required, StringLength(200, MinimumLength = 3)]
    public string VenueName { get; set; } = string.Empty;

    [Required, StringLength(500, MinimumLength = 5)]
    public string Address { get; set; } = string.Empty;

    public TimeOnly OpenTime { get; set; }
    public TimeOnly CloseTime { get; set; }

    [Phone, StringLength(30)]
    public string? PhoneNumber { get; set; }

    [Range(-90, 90)]
    public double? Latitude { get; set; }

    [Range(-180, 180)]
    public double? Longitude { get; set; }

    [Range(0, 100_000_000)]
    public double BasePrice { get; set; }

    [Range(0, 100)]
    public int InitialCourtCount { get; set; }

    public List<string> Amenities { get; set; } = [];
}

public class OwnerCourtUpsertRequest
{
    [Range(1, 10_000)]
    public int CourtNumber { get; set; }

    [StringLength(100)]
    public string? SurfaceType { get; set; }

    public bool IsIndoor { get; set; }

    [Required, RegularExpression("^(Available|Maintenance|Inactive)$")]
    public string AvailabilityStatus { get; set; } = "Available";
}

public class OwnerScheduleBlockRequest
{
    public int CourtId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
}

public class OwnerBookingStatusRequest
{
    [Required, RegularExpression("^(Confirmed|Cancelled)$")]
    public string Status { get; set; } = string.Empty;
}

public class OwnerVenueResponse
{
    public int VenueId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public double OverallRating { get; set; }
    public TimeOnly OpenTime { get; set; }
    public TimeOnly CloseTime { get; set; }
    public string? PhoneNumber { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double BasePrice { get; set; }
    public List<string> Amenities { get; set; } = [];
    public List<OwnerCourtResponse> Courts { get; set; } = [];
}

public class OwnerCourtResponse
{
    public int CourtId { get; set; }
    public int VenueId { get; set; }
    public int CourtNumber { get; set; }
    public string? SurfaceType { get; set; }
    public bool IsIndoor { get; set; }
    public string AvailabilityStatus { get; set; } = string.Empty;
}

public class OwnerScheduleResponse
{
    public DateOnly Date { get; set; }
    public List<OwnerVenueResponse> Venues { get; set; } = [];
    public List<OwnerScheduleItemResponse> Items { get; set; } = [];
}

public class OwnerScheduleItemResponse
{
    public int BookingId { get; set; }
    public int CourtId { get; set; }
    public int VenueId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public int CourtNumber { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public double Amount { get; set; }
    public string? PaymentStatus { get; set; }
    public bool IsOwnerBlock { get; set; }
}
