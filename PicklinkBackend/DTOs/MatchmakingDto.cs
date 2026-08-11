using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using PicklinkBackend.Services.Shared;

namespace PicklinkBackend.DTOs;

public class JoinSoloQueueRequest : IValidatableObject
{
    [StringLength(150)]
    public string? Title { get; set; }

    [Range(2, 8)]
    public int? PlayerCount { get; set; }

    [Range(1, 5)]
    public int? MinSkillLevel { get; set; }

    [Range(1, 5)]
    public int? MaxSkillLevel { get; set; }

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
        if (MinSkillLevel.HasValue && MaxSkillLevel.HasValue && MinSkillLevel > MaxSkillLevel)
        {
            yield return new ValidationResult(
                "Trình độ tối thiểu không thể lớn hơn trình độ tối đa.",
                new[] { nameof(MinSkillLevel), nameof(MaxSkillLevel) });
        }

        if (SearchLatitude.HasValue != SearchLongitude.HasValue)
        {
            yield return new ValidationResult(
                "Vị trí vĩ độ và kinh độ phải được cung cấp cùng nhau.",
                new[] { nameof(SearchLatitude), nameof(SearchLongitude) });
        }

        if (QueueSlots is null || ReplayType is not ("None" or "Daily" or "Weekly" or "Monthly"))
            yield break;

        var now = VietnamTime.Now;
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
                    $"Khung giờ thứ {index + 1} không phù hợp với kiểu lặp lại '{ReplayType}'.",
                    new[] { nameof(QueueSlots) });
            }

            if (!TimeOnly.TryParseExact(slot.TimeStart, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
                !TimeOnly.TryParseExact(slot.TimeEnd, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
            {
                yield return new ValidationResult(
                    $"Khung giờ thứ {index + 1} phải theo định dạng giờ HH:mm (ví dụ: 18:00).",
                    new[] { nameof(QueueSlots) });
                continue;
            }

            var startMin = start.Hour * 60 + start.Minute;
            var endMin = (end == TimeOnly.MinValue && start > TimeOnly.MinValue) ? 24 * 60 : end.Hour * 60 + end.Minute;

            if (startMin >= endMin)
            {
                yield return new ValidationResult(
                    $"Khung giờ thứ {index + 1} không hợp lệ: giờ kết thúc ({slot.TimeEnd}) phải sau giờ bắt đầu ({slot.TimeStart}). Slot trong ngày không được qua đêm.",
                    new[] { nameof(QueueSlots) });
            }

            if (ReplayType == "None" && slot.SpecificDate is { } date &&
                (date < today || (date == today && startMin <= currentTime.Hour * 60 + currentTime.Minute)))
            {
                yield return new ValidationResult(
                    $"Khung giờ thứ {index + 1} cho ngày hôm nay ({slot.TimeStart} - {slot.TimeEnd}) đã trôi qua. Vui lòng chọn khung giờ trong tương lai.",
                    new[] { nameof(QueueSlots) });
            }
        }

        var validParsedSlots = QueueSlots
            .Select(s => (
                StartOk: TimeOnly.TryParseExact(s.TimeStart, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var st),
                EndOk: TimeOnly.TryParseExact(s.TimeEnd, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var en),
                StartMin: TimeOnly.TryParseExact(s.TimeStart, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var stVal) ? stVal.Hour * 60 + stVal.Minute : 0,
                EndMin: TimeOnly.TryParseExact(s.TimeEnd, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var enVal)
                    ? ((enVal == TimeOnly.MinValue && stVal > TimeOnly.MinValue) ? 24 * 60 : enVal.Hour * 60 + enVal.Minute)
                    : 0,
                TimeStartStr: s.TimeStart,
                TimeEndStr: s.TimeEnd,
                DateKey: s.SpecificDate?.ToString("yyyy-MM-dd") ?? s.DayOfWeek?.ToString() ?? s.DayOfMonth?.ToString() ?? "daily"
            ))
            .Where(s => s.StartOk && s.EndOk && s.StartMin < s.EndMin)
            .GroupBy(s => s.DateKey);

        foreach (var group in validParsedSlots)
        {
            var sorted = group.OrderBy(s => s.StartMin).ToList();
            var blocks = new List<(int StartMin, int EndMin, string StartStr, string EndStr)>();
            foreach (var item in sorted)
            {
                if (blocks.Count == 0)
                {
                    blocks.Add((item.StartMin, item.EndMin, item.TimeStartStr, item.TimeEndStr));
                }
                else
                {
                    var lastIndex = blocks.Count - 1;
                    var current = blocks[lastIndex];
                    if (item.StartMin <= current.EndMin)
                    {
                        if (item.EndMin > current.EndMin)
                        {
                            blocks[lastIndex] = (current.StartMin, item.EndMin, current.StartStr, item.TimeEndStr);
                        }
                    }
                    else
                    {
                        blocks.Add((item.StartMin, item.EndMin, item.TimeStartStr, item.TimeEndStr));
                    }
                }
            }

            foreach (var block in blocks)
            {
                if (block.EndMin - block.StartMin < 30)
                {
                    yield return new ValidationResult(
                        $"Chuỗi khung giờ chơi liên tục ({block.StartStr} - {block.EndStr}) phải kéo dài ít nhất 30 phút.",
                        new[] { nameof(QueueSlots) });
                }
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
                "Hàng chờ chơi một lần chỉ được chọn tối đa 31 ngày.",
                new[] { nameof(QueueSlots) });
        }

        if (ReplayType == "None"
            && oneOffDates.Count > 1
            && oneOffDates[^1].DayNumber - oneOffDates[0].DayNumber > 30)
        {
            yield return new ValidationResult(
                "Khoảng thời gian chơi một lần không được vượt quá 31 ngày liên tiếp.",
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
                "Chỉ được đăng ký tối đa 20 khung giờ cho cùng một ngày.",
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
                "Các khung giờ chơi trong cùng một ngày không được trùng hoặc chồng thời gian lên nhau.",
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
    public bool IsCurrentPlayer { get; set; }
    public string Status { get; set; } = "Approved";
}

public class QueueStatusResponse
{
    public bool InQueue { get; set; }
    public int? MatchmakingQueueId { get; set; }
    public int? MatchId { get; set; }
    public string? Title { get; set; }
    public int PlayerCount { get; set; }
    public string? MatchType { get; set; }
    public int MinSkillLevel { get; set; }
    public int MaxSkillLevel { get; set; }
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
