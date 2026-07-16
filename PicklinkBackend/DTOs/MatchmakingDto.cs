using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;

namespace PicklinkBackend.DTOs;

public class JoinSoloQueueRequest : IValidatableObject
{
    public const int MaxQueueSlots = 31 * 20;

    [Required, RegularExpression("^(1vs1|2vs2)$")]
    public string MatchType { get; set; } = null!; // "1vs1" or "2vs2"

    [Range(0.5, 10)]
    public double SearchRadiusKm { get; set; } = 5;

    [Range(-90d, 90d)]
    public double? SearchLatitude { get; set; }

    [Range(-180d, 180d)]
    public double? SearchLongitude { get; set; }

    [Required, RegularExpression("^(None|Daily|Weekly|Monthly)$")]
    public string ReplayType { get; set; } = "None"; // "None", "Daily", "Weekly", "Monthly"

    [StringLength(100)]
    public string? ReplayWeekdays { get; set; } // e.g. "Monday,Thursday"

    public bool IsPublic { get; set; } = false;

    public bool IsActive { get; set; } = true;

    [StringLength(150)]
    public string? Province { get; set; }

    [StringLength(150)]
    public string? Ward { get; set; }

    [StringLength(500)]
    public string? SharedVenues { get; set; }

    [Required, MinLength(1), MaxLength(MaxQueueSlots)]
    public List<QueueSlotRequest> QueueSlots { get; set; } = new();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (SearchLatitude.HasValue != SearchLongitude.HasValue)
        {
            yield return new ValidationResult(
                "SearchLatitude and SearchLongitude must be provided together.",
                new[] { nameof(SearchLatitude), nameof(SearchLongitude) });
        }

        if (QueueSlots is null || ReplayType is not ("None" or "Daily" or "Weekly" or "Monthly"))
            yield break;

        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        var currentTime = TimeOnly.FromDateTime(now);

        for (var index = 0; index < QueueSlots.Count; index++)
        {
            var slot = QueueSlots[index];
            var hasExpectedDateShape = ReplayType switch
            {
                "None" => slot.SpecificDate.HasValue && !slot.DayOfWeek.HasValue && !slot.DayOfMonth.HasValue,
                "Daily" => !slot.SpecificDate.HasValue && !slot.DayOfWeek.HasValue && !slot.DayOfMonth.HasValue,
                "Weekly" => !slot.SpecificDate.HasValue && slot.DayOfWeek.HasValue && !slot.DayOfMonth.HasValue,
                "Monthly" => !slot.SpecificDate.HasValue && !slot.DayOfWeek.HasValue && slot.DayOfMonth.HasValue,
                _ => false
            };

            if (!hasExpectedDateShape || (slot.DayOfWeek.HasValue && !Enum.IsDefined(slot.DayOfWeek.Value)))
            {
                yield return new ValidationResult(
                    $"QueueSlots[{index}] does not match ReplayType '{ReplayType}'.",
                    new[] { nameof(QueueSlots) });
            }

            if (!TimeOnly.TryParseExact(slot.TimeStart, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
                !TimeOnly.TryParseExact(slot.TimeEnd, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
            {
                yield return new ValidationResult(
                    $"QueueSlots[{index}] times must use the HH:mm format.",
                    new[] { nameof(QueueSlots) });
                continue;
            }

            if (end - start < TimeSpan.FromMinutes(90))
            {
                yield return new ValidationResult(
                    $"QueueSlots[{index}] must be at least 90 minutes long.",
                    new[] { nameof(QueueSlots) });
            }

            if (ReplayType == "None" && slot.SpecificDate is { } date &&
                (date < today || (date == today && start <= currentTime)))
            {
                yield return new ValidationResult(
                    $"QueueSlots[{index}] starts in the past.",
                    new[] { nameof(QueueSlots) });
            }
        }

        var oneOffDates = QueueSlots
            .Where(slot => slot.SpecificDate.HasValue)
            .Select(slot => slot.SpecificDate!.Value)
            .Distinct()
            .OrderBy(date => date)
            .ToList();

        if (ReplayType == "None" && oneOffDates.Count > 31)
        {
            yield return new ValidationResult(
                "A one-off queue can cover at most 31 dates.",
                new[] { nameof(QueueSlots) });
        }

        if (ReplayType == "None"
            && oneOffDates.Count > 1
            && oneOffDates[^1].DayNumber - oneOffDates[0].DayNumber > 30)
        {
            yield return new ValidationResult(
                "A one-off queue date range cannot exceed 31 consecutive dates.",
                new[] { nameof(QueueSlots) });
        }

        var hasTooManySlotsForOneDate = ReplayType switch
        {
            "None" => QueueSlots.Where(s => s.SpecificDate.HasValue).GroupBy(s => s.SpecificDate).Any(g => g.Count() > 20),
            "Daily" => QueueSlots.Count > 20,
            "Weekly" => QueueSlots.Where(s => s.DayOfWeek.HasValue).GroupBy(s => s.DayOfWeek).Any(g => g.Count() > 20),
            "Monthly" => QueueSlots.Where(s => s.DayOfMonth.HasValue).GroupBy(s => s.DayOfMonth).Any(g => g.Count() > 20),
            _ => false
        };

        if (hasTooManySlotsForOneDate)
        {
            yield return new ValidationResult(
                "A queue can contain at most 20 slots for the same date rule.",
                new[] { nameof(QueueSlots) });
        }

        var hasOverlappingSlots = ReplayType switch
        {
            "None" => QueueSlots
                .Where(slot => slot.SpecificDate.HasValue)
                .GroupBy(slot => slot.SpecificDate)
                .Any(group => HasOverlap(group)),
            "Daily" => HasOverlap(QueueSlots),
            "Weekly" => QueueSlots
                .Where(slot => slot.DayOfWeek.HasValue)
                .GroupBy(slot => slot.DayOfWeek)
                .Any(group => HasOverlap(group)),
            "Monthly" => QueueSlots
                .Where(slot => slot.DayOfMonth.HasValue)
                .GroupBy(slot => slot.DayOfMonth)
                .Any(group => HasOverlap(group)),
            _ => false
        };

        if (hasOverlappingSlots)
        {
            yield return new ValidationResult(
                "Queue slots for the same date rule cannot overlap.",
                new[] { nameof(QueueSlots) });
        }
    }

    private static bool HasOverlap(IEnumerable<QueueSlotRequest> slots)
    {
        var ordered = slots
            .Select(slot =>
            {
                var hasStart = TimeOnly.TryParseExact(
                    slot.TimeStart,
                    "HH:mm",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var start);
                var hasEnd = TimeOnly.TryParseExact(
                    slot.TimeEnd,
                    "HH:mm",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var end);
                return (IsValid: hasStart && hasEnd, Start: start, End: end);
            })
            .Where(slot => slot.IsValid)
            .OrderBy(slot => slot.Start)
            .ToList();

        return ordered
            .Zip(ordered.Skip(1), (left, right) => right.Start < left.End)
            .Any(overlaps => overlaps);
    }
}

public class QueueSlotRequest
{
    public DayOfWeek? DayOfWeek { get; set; }

    public DateOnly? SpecificDate { get; set; }

    [Range(1, 31)]
    public int? DayOfMonth { get; set; }

    [Required, RegularExpression("^(?:[01][0-9]|2[0-3]):[0-5][0-9]$")]
    public string TimeStart { get; set; } = null!; // Format: "HH:mm"

    [Required, RegularExpression("^(?:[01][0-9]|2[0-3]):[0-5][0-9]$")]
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
