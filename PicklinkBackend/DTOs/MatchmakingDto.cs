using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace PicklinkBackend.DTOs;

public class JoinSoloQueueRequest
{
    [Required]
    public string MatchType { get; set; } = null!; // "1vs1" or "2vs2"

    [Range(0.5, 10)]
    public double SearchRadiusKm { get; set; } = 5;

    public double? SearchLatitude { get; set; }

    public double? SearchLongitude { get; set; }

    public string ReplayType { get; set; } = "None"; // "None", "Daily", "Weekly", "Monthly"

    public string? ReplayWeekdays { get; set; } // e.g. "Monday,Thursday"

    public bool IsPublic { get; set; } = false;

    public bool IsActive { get; set; } = true;

    public string? Province { get; set; }

    public string? Ward { get; set; }

    public string? SharedVenues { get; set; }

    [Required, MinLength(1)]
    public List<QueueSlotRequest> QueueSlots { get; set; } = new();
}

public class QueueSlotRequest
{
    public DayOfWeek? DayOfWeek { get; set; }

    public DateOnly? SpecificDate { get; set; }

    [Range(1, 31)]
    public int? DayOfMonth { get; set; }

    [Required]
    public string TimeStart { get; set; } = null!; // Format: "HH:mm"

    [Required]
    public string TimeEnd { get; set; } = null!; // Format: "HH:mm"
}

public class QueueSlotResponse
{
    public DayOfWeek? DayOfWeek { get; set; }
    public DateOnly? SpecificDate { get; set; }
    public int? DayOfMonth { get; set; }
    public string TimeStart { get; set; } = null!;
    public string TimeEnd { get; set; } = null!;
}

public class QueuePlayerResponse
{
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = null!;
    public string? AvatarUrl { get; set; }
    public bool IsHost { get; set; }
}

public class QueueStatusResponse
{
    public bool InQueue { get; set; }
    public int? MatchmakingQueueId { get; set; }
    public string? MatchType { get; set; }
    public int? SkillLevel { get; set; }
    public double SearchRadiusKm { get; set; }
    public double? SearchLatitude { get; set; }
    public double? SearchLongitude { get; set; }
    public bool IsActive { get; set; }
    public string ReplayType { get; set; } = "None";
    public string? ReplayWeekdays { get; set; }
    public int? ConversationId { get; set; }
    public bool IsPublic { get; set; }
    public string? Province { get; set; }
    public string? Ward { get; set; }
    public string? SharedVenues { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? CreatedAt { get; set; }
    public List<QueueSlotResponse> QueueSlots { get; set; } = new();
    public List<QueuePlayerResponse> QueuePlayers { get; set; } = new();
}
