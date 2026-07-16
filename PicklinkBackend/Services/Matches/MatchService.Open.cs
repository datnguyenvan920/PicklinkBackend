using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Services.Bookings;
using PicklinkBackend.Services.Schedules;
using PicklinkBackend.Services.Shared;

namespace PicklinkBackend.Services.Matches;

public partial class MatchService
{
    private static readonly string[] InactiveBookingStatuses = ["Cancelled", "Expired"];
    public async Task<ServiceResult<OpenMatchDetailResponse>> CreateOpenMatch(
        CreateOpenMatchRequest request,
        CancellationToken cancellationToken)
    {
        var player = await CurrentPlayerAsync(cancellationToken);
        if (player is null) return BadRequest(new { message = "TÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â i khoÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â£n chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°a cÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â³ hÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã…â€œ sÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â¡ ngÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â¡i." });

        var matchType = NormalizeMatchType(request.MatchType);
        if (matchType is null)
            return BadRequest(new { message = "HÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¬nh thÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â©c trÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­n chÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â° nhÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­n 1vs1 hoÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â·c 2vs2." });
        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new { message = "Vui lÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng nhÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­p tiÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Âªu ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â lÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi mÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi." });
        if (string.IsNullOrWhiteSpace(request.Province) || string.IsNullOrWhiteSpace(request.Ward))
            return BadRequest(new { message = "Vui lÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng nhÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­p tÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â°nh/thÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â nh phÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ vÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â  xÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â£/phÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âng." });

        if (request.AvailabilitySlots.Count > 20)
            return BadRequest(new { message = "MÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Âi lÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi mÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â£c chÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Ân tÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“i ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“a 20 slot." });
        var availabilitySlots = new List<MatchAvailabilitySlot>();
        foreach (var requestedSlot in request.AvailabilitySlots)
        {
            if (!TryParseMatchTime(requestedSlot.TimeStart, out var slotStart)
                || !TryParseMatchTime(requestedSlot.TimeEnd, out var slotEnd))
                return BadRequest(new { message = "GiÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â cÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â§a mÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Âi slot phÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â£i cÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â³ ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¹nh dÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¡ng HH:mm, vÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â­ dÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â¥ 18:00." });
            if (slotEnd <= slotStart)
                return BadRequest(new { message = "GiÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â kÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¿t thÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Âºc cÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â§a mÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Âi slot phÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â£i sau giÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â bÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¯t ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â§u." });
            availabilitySlots.Add(new MatchAvailabilitySlot
            {
                TimeStart = slotStart,
                TimeEnd = slotEnd
            });
        }
        availabilitySlots = availabilitySlots
            .OrderBy(item => item.TimeStart)
            .ToList();
        for (var index = 1; index < availabilitySlots.Count; index++)
        {
            var previous = availabilitySlots[index - 1];
            var current = availabilitySlots[index];
            if (current.TimeStart < previous.TimeEnd)
                return BadRequest(new { message = "CÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¡c slot khÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â£c trÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¹ng hoÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â·c chÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã…â€œng thÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi gian." });
        }

        var availableDateFrom = request.AvailableDateFrom;
        var availableDateTo = request.AvailableDateTo;
        if (availableDateFrom < DateOnly.FromDateTime(DateTime.Today))
            return BadRequest(new { message = "NgÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â y bÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¯t ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â§u cÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â³ thÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€ Ã¢â‚¬â„¢ chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â¡i khÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â£c ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€¦Ã‚Â¸ quÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¡ khÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â©." });
        if (availableDateTo < availableDateFrom)
            return BadRequest(new { message = "NgÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â y kÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¿t thÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Âºc phÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â£i tÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â« ngÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â y bÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¯t ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â§u trÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€¦Ã‚Â¸ ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“i." });
        if (availableDateTo.DayNumber - availableDateFrom.DayNumber > 60)
            return BadRequest(new { message = "KhoÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â£ng ngÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â y cÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â³ thÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€ Ã¢â‚¬â„¢ chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â¡i khÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â£c dÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â i quÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¡ 60 ngÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â y." });
        if (availableDateFrom == DateOnly.FromDateTime(DateTime.Today)
            && availabilitySlots.Any(item => item.TimeStart <= TimeOnly.FromDateTime(DateTime.Now)))
            return BadRequest(new { message = "GiÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â bÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¯t ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â§u cÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â§a mÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Âi slot trong hÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´m nay phÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â£i lÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‚Âºn hÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â¡n giÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â hiÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¡n tÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¡i." });
        TimeOnly preferredTimeStart;
        TimeOnly preferredTimeEnd;
        if (availabilitySlots.Count > 0)
        {
            preferredTimeStart = availabilitySlots.Min(item => item.TimeStart);
            preferredTimeEnd = availabilitySlots.Max(item => item.TimeEnd);
        }
        else
        {
            if (!TryParseMatchTime(request.PreferredTimeStart, out preferredTimeStart)
                || !TryParseMatchTime(request.PreferredTimeEnd, out preferredTimeEnd))
                return BadRequest(new { message = "GiÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â¡i phÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â£i cÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â³ ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¹nh dÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¡ng HH:mm, vÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â­ dÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â¥ 18:00." });
            if (preferredTimeEnd <= preferredTimeStart)
                return BadRequest(new { message = "GiÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â kÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¿t thÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Âºc mong muÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“n phÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â£i sau giÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â bÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¯t ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â§u." });
        }
        if (request.MinSkillLevel > request.MaxSkillLevel)
            return BadRequest(new { message = "TrÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¬nh ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢ tÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“i ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“a khÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â£c nhÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â hÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â¡n trÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¬nh ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢ tÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“i thiÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€ Ã¢â‚¬â„¢u." });

        var preferredVenueIds = request.PreferredVenueIds.Where(id => id > 0).Distinct().ToList();
        if (preferredVenueIds.Count == 0)
            return BadRequest(new { message = "Vui lÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng chÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Ân ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â­t nhÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¥t mÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢t cÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â¥m sÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢n mong muÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“n." });

        var preferredVenues = await _db.Venues.AsNoTracking()
            .Where(venue => preferredVenueIds.Contains(venue.VenueId)
                && venue.ApprovalStatus == "Approved"
                && venue.IsOpen
                && venue.Courts.Any(court => court.AvailabilityStatus == "Available"))
            .Select(venue => new { venue.VenueId, venue.Latitude, venue.Longitude })
            .ToListAsync(cancellationToken);
        if (preferredVenues.Count != preferredVenueIds.Count)
            return BadRequest(new { message = "MÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢t hoÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â·c nhiÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âu cÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â¥m sÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢n ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â£ chÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Ân hiÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¡n khÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng cÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²n hoÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¡t ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢ng." });

        var now = DateTime.UtcNow;
        var match = new Match
        {
            HostPlayerId = player.PlayerId,
            MatchType = matchType,
            MatchSkillLevel = request.MinSkillLevel,
            MinSkillLevel = request.MinSkillLevel,
            MaxSkillLevel = request.MaxSkillLevel,
            RequiredPlayerCount = request.NeededPlayerCount + 1,
            Status = "Recruiting",
            Title = request.Title.Trim(),
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            Province = request.Province.Trim(),
            Ward = request.Ward.Trim(),
            SearchRadiusKm = request.SearchRadiusKm,
            SearchLatitude = request.SearchLatitude,
            SearchLongitude = request.SearchLongitude,
            AvailableDateFrom = availableDateFrom,
            AvailableDateTo = availableDateTo,
            PreferredTimeStart = preferredTimeStart,
            PreferredTimeEnd = preferredTimeEnd,
            SharedVenues = string.Join(",", preferredVenueIds),
            CreatedAt = now
        };
        foreach (var availabilitySlot in availabilitySlots)
            match.AvailabilitySlots.Add(availabilitySlot);
        match.MatchParticipants.Add(new MatchParticipant
        {
            PlayerId = player.PlayerId,
            Status = "Approved",
            IsHost = true,
            RequestedAt = now,
            RespondedAt = now
        });
        var conversation = new Conversation
        {
            Match = match,
            ConversationType = "LobbyChat",
            ConversationName = request.Title.Trim(),
            CreatedAt = now
        };
        conversation.ConversationParticipants.Add(new ConversationParticipant
        {
            UserId = player.UserId,
            JoinedAt = now
        });
        _db.Conversations.Add(conversation);
        await _db.SaveChangesAsync(cancellationToken);

        _matchRealtime.Publish(match.MatchId, "Created");
        var response = await LoadOpenMatchResponseAsync(match.MatchId, player.PlayerId, cancellationToken);
        return response is null
            ? StatusCode(500, new { message = "KhÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng thÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€ Ã¢â‚¬â„¢ tÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â£i lÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¡i phÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng ghÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â©p trÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­n vÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â«a tÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¡o." })
            : CreatedAtAction(
                nameof(GetOpenMatchDetail),
                new { matchId = match.MatchId },
                response);
    }
    public async Task<ServiceResult<List<MatchPreferredVenueResponse>>> SearchPreferredVenues(
        string? province,
        string? ward,
        double radiusKm = 5,
        double? latitude = null,
        double? longitude = null,
        CancellationToken cancellationToken = default)
    {
        if (radiusKm is < 0.5 or > 10)
            return BadRequest(new { message = "BÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¡n kÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â­nh tÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¬m sÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢n phÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â£i tÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â« 0,5 ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¿n 10 km." });
        if (latitude.HasValue != longitude.HasValue)
            return BadRequest(new { message = "CÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â§n cung cÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¥p ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã…â€œng thÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi vÃƒÆ’Ã¢â‚¬Å¾Ãƒâ€šÃ‚Â© ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢ vÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â  kinh ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢." });

        var provinceText = province?.Trim();
        var wardText = ward?.Trim();
        var query = _db.Venues.AsNoTracking()
            .Where(venue => venue.ApprovalStatus == "Approved"
                && venue.IsOpen
                && venue.Courts.Any(court => court.AvailabilityStatus == "Available"));
        if (!string.IsNullOrWhiteSpace(provinceText))
            query = query.Where(venue => venue.Address.Contains(provinceText));
        if (!string.IsNullOrWhiteSpace(wardText))
            query = query.Where(venue => venue.Address.Contains(wardText));

        var rows = await query
            .Select(venue => new
            {
                venue.VenueId,
                venue.VenueName,
                venue.Address,
                venue.Latitude,
                venue.Longitude
            })
            .Take(200)
            .ToListAsync(cancellationToken);

        var result = rows.Select(venue =>
        {
            double? distance = null;
            if (latitude.HasValue && longitude.HasValue && venue.Latitude.HasValue && venue.Longitude.HasValue)
                distance = DistanceKm(latitude.Value, longitude.Value, venue.Latitude.Value, venue.Longitude.Value);
            return new MatchPreferredVenueResponse
            {
                VenueId = venue.VenueId,
                VenueName = venue.VenueName,
                Address = venue.Address,
                Latitude = venue.Latitude,
                Longitude = venue.Longitude,
                DistanceKm = distance.HasValue ? Math.Round(distance.Value, 2) : null
            };
        });
        if (latitude.HasValue && longitude.HasValue)
            result = result.Where(venue => venue.DistanceKm.HasValue && venue.DistanceKm <= radiusKm);

        return Ok(result
            .OrderBy(venue => venue.DistanceKm ?? double.MaxValue)
            .ThenBy(venue => venue.VenueName)
            .Take(100)
            .ToList());
    }
    public async Task<ServiceResult<PaginatedResponse<MatchSearchResponse>>> GetOpenMatches(
        string? owner,
        string? matchType,
        int? skillLevel,
        DateOnly? from,
        DateOnly? to,
        string? province,
        string? ward,
        int page = 1,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var normalizedOwner = string.IsNullOrWhiteSpace(owner)
            ? null
            : owner.Trim().ToLowerInvariant();
        if (normalizedOwner is not null and not "mine" and not "other")
            return BadRequest(new { message = "BÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢ lÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âc chÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â§ phÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng chÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â° nhÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­n mine hoÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â·c other." });
        var normalizedType = string.IsNullOrWhiteSpace(matchType) ? null : NormalizeMatchType(matchType);
        if (!string.IsNullOrWhiteSpace(matchType) && normalizedType is null)
            return BadRequest(new { message = "HÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¬nh thÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â©c trÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­n chÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â° nhÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­n 1vs1 hoÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â·c 2vs2." });
        if (skillLevel is < 1 or > 5)
            return BadRequest(new { message = "TrÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¬nh ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢ phÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â£i tÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â« 1 ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¿n 5." });

        var currentPlayerId = await CurrentPlayerIdAsync(cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var query = MatchSearchQuery(asNoTracking: true)
            .Where(match => match.HostPlayerId != null
                && match.Status == "Recruiting"
                && match.AvailableDateTo >= today);
        if (normalizedOwner == "mine")
            query = query.Where(match => match.HostPlayerId == currentPlayerId);
        else if (normalizedOwner == "other" && currentPlayerId.HasValue)
            query = query.Where(match => match.HostPlayerId != currentPlayerId);
        if (normalizedType is not null) query = query.Where(match => match.MatchType == normalizedType);
        if (skillLevel.HasValue)
            query = query.Where(match => match.MinSkillLevel <= skillLevel && match.MaxSkillLevel >= skillLevel);
        if (from.HasValue) query = query.Where(match => match.AvailableDateTo >= from.Value);
        if (to.HasValue) query = query.Where(match => match.AvailableDateFrom <= to.Value);
        if (!string.IsNullOrWhiteSpace(province))
            query = query.Where(match => match.Province != null && match.Province.Contains(province.Trim()));
        if (!string.IsNullOrWhiteSpace(ward))
            query = query.Where(match => match.Ward != null && match.Ward.Contains(ward.Trim()));

        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);
        var totalCount = await query.CountAsync(cancellationToken);
        var matches = await query
            .OrderBy(match => match.AvailableDateFrom)
            .ThenBy(match => match.PreferredTimeStart)
            .ThenBy(match => match.MatchId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var venueLookup = await LoadPreferredVenueLookupAsync(matches, cancellationToken);
        return Ok(Pagination.Create(
            matches.Select(match => MapSearchResponse(match, currentPlayerId, venueLookup)),
            totalCount,
            page,
            pageSize));
    }
    public async Task<ServiceResult<PaginatedResponse<MatchSearchResponse>>> GetMyOpenMatches(
        int page = 1,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var playerId = await CurrentPlayerIdAsync(cancellationToken);
        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);
        if (playerId is null)
            return Ok(Pagination.Create(Array.Empty<MatchSearchResponse>(), 0, page, pageSize));

        var query = MyMatchesQuery(asNoTracking: true)
            .Where(match => match.HostPlayerId != null
                && match.MatchParticipants.Any(participant =>
                    participant.PlayerId == playerId
                    && participant.Status != "Rejected"
                    && participant.Status != "Withdrawn"
                    && participant.Status != "Left"
                    && participant.Status != "Removed"));
        var totalCount = await query.CountAsync(cancellationToken);
        var matches = await query
            .OrderByDescending(match => match.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var venueLookup = new Dictionary<int, MatchPreferredVenueResponse>();
        return Ok(Pagination.Create(
            matches.Select(match => MapSearchResponse(match, playerId, venueLookup)),
            totalCount,
            page,
            pageSize));
    }
    public async Task<ServiceResult<OpenMatchDetailResponse>> GetOpenMatchDetail(
        int matchId,
        CancellationToken cancellationToken)
    {
        var playerId = await CurrentPlayerIdAsync(cancellationToken);
        var response = await LoadOpenMatchResponseAsync(matchId, playerId, cancellationToken);
        return response is null ? NotFound(new { message = "KhÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng tÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¬m thÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¥y phÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng ghÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â©p trÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­n." }) : Ok(response);
    }
    public async Task<ServiceResult<OpenMatchDetailResponse>> JoinOpenMatch(
        int matchId,
        CancellationToken cancellationToken)
    {
        var player = await CurrentPlayerAsync(cancellationToken);
        if (player is null) return BadRequest(new { message = "TÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â i khoÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â£n chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°a cÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â³ hÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã…â€œ sÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â¡ ngÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â¡i." });

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_db, transaction, $"match-roster:{matchId}", cancellationToken))
            return Conflict(new { message = "Danh sÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¡ch ngÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â¡i ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ang ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â£c cÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­p nhÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­t." });

        var match = await MatchInvitationQuery().SingleOrDefaultAsync(item => item.MatchId == matchId, cancellationToken);
        if (match is null) return NotFound(new { message = "KhÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng tÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¬m thÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¥y phÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng ghÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â©p trÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­n." });
        if (match.HostPlayerId == player.PlayerId)
            return Conflict(new { message = "BÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¡n lÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â  chÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â§ phÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng ghÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â©p trÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­n." });
        if (match.Status != "Recruiting")
            return Conflict(new { message = "PhÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng hiÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¡n khÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng nhÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­n thÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Âªm yÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Âªu cÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â§u tham gia." });
        if (ApprovedParticipants(match).Count >= match.RequiredPlayerCount)
            return Conflict(new { message = "PhÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â£ ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â§ ngÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi." });
        if (player.SkillLevel < match.MinSkillLevel || player.SkillLevel > match.MaxSkillLevel)
            return Conflict(new { message = $"TrÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¬nh ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢ cÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â§a bÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¡n chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°a nÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â±m trong khoÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â£ng {match.MinSkillLevel}ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã…â€œ{match.MaxSkillLevel} cÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â§a lÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi mÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi." });

        var participant = match.MatchParticipants.SingleOrDefault(item => item.PlayerId == player.PlayerId);
        if (participant?.Status is "Approved" or "Accepted" or "Pending")
        {
            await transaction.RollbackAsync(cancellationToken);
            return Ok(await LoadOpenMatchResponseAsync(matchId, player.PlayerId, cancellationToken));
        }

        if (participant is null)
        {
            participant = new MatchParticipant
            {
                MatchId = match.MatchId,
                PlayerId = player.PlayerId,
                Status = "Pending",
                RequestedAt = DateTime.UtcNow
            };
            _db.MatchParticipants.Add(participant);
        }
        else
        {
            participant.Status = "Pending";
            participant.IsHost = false;
            participant.RequestedAt = DateTime.UtcNow;
            participant.RespondedAt = null;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _matchRealtime.Publish(matchId, "JoinRequested");
        return Ok(await LoadOpenMatchResponseAsync(matchId, player.PlayerId, cancellationToken));
    }
    public async Task<ServiceResult<OpenMatchDetailResponse>> LeaveOpenMatch(
        int matchId,
        CancellationToken cancellationToken)
    {
        var player = await CurrentPlayerAsync(cancellationToken);
        if (player is null) return BadRequest(new { message = "TÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â i khoÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â£n chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°a cÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â³ hÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã…â€œ sÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â¡ ngÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â¡i." });

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_db, transaction, $"match-roster:{matchId}", cancellationToken))
            return Conflict(new { message = "Danh sÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¡ch ngÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â¡i ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ang ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â£c cÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­p nhÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­t." });

        var match = await MatchInvitationQuery().SingleOrDefaultAsync(item => item.MatchId == matchId, cancellationToken);
        if (match is null) return NotFound(new { message = "KhÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng tÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¬m thÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¥y phÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng ghÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â©p trÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­n." });
        if (match.HostPlayerId == player.PlayerId)
            return Conflict(new { message = "ChÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â§ phÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng cÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â§n hÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â§y lÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi mÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi thay vÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¬ rÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi phÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng." });
        if (match.Status is "BookingPending" or "Booked" or "Completed")
            return Conflict(new { message = "KhÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng thÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€ Ã¢â‚¬â„¢ rÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi phÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng sau khi chÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â§ phÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â£ tÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¡o booking." });

        var participant = match.MatchParticipants.SingleOrDefault(item => item.PlayerId == player.PlayerId);
        if (participant is null || participant.Status is "Withdrawn" or "Left" or "Rejected" or "Removed")
            return Conflict(new { message = "BÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¡n khÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng cÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â³ yÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Âªu cÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â§u tham gia ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ang hoÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¡t ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢ng." });

        participant.Status = "Withdrawn";
        participant.RespondedAt = DateTime.UtcNow;
        if (match.Status == "ReadyToBook") match.Status = "Recruiting";
        await RemoveConversationParticipantAsync(match, player.UserId, cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _matchRealtime.Publish(matchId, "ParticipantWithdrawn");
        return Ok(await LoadOpenMatchResponseAsync(matchId, player.PlayerId, cancellationToken));
    }
    public async Task<ServiceResult<OpenMatchDetailResponse>> AcceptParticipant(
        int matchId,
        int participantId,
        CancellationToken cancellationToken)
    {
        var hostPlayerId = await CurrentPlayerIdAsync(cancellationToken);
        if (hostPlayerId is null) return BadRequest(new { message = "TÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â i khoÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â£n chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°a cÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â³ hÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã…â€œ sÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â¡ ngÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â¡i." });

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_db, transaction, $"match-roster:{matchId}", cancellationToken))
            return Conflict(new { message = "Danh sÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¡ch ngÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â¡i ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ang ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â£c cÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­p nhÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­t." });

        var match = await MatchInvitationQuery().SingleOrDefaultAsync(item => item.MatchId == matchId, cancellationToken);
        if (match is null) return NotFound(new { message = "KhÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng tÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¬m thÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¥y phÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng ghÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â©p trÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­n." });
        if (match.HostPlayerId != hostPlayerId) return Forbid();
        if (match.Status != "Recruiting")
            return Conflict(new { message = "ChÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â° cÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â³ thÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€ Ã¢â‚¬â„¢ duyÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¡t thÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â nh viÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Âªn khi phÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ang tuyÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€ Ã¢â‚¬â„¢n ngÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi." });

        var participant = match.MatchParticipants.SingleOrDefault(item => item.ParticipantId == participantId);
        if (participant is null || participant.Status != "Pending")
            return Conflict(new { message = "YÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Âªu cÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â§u tham gia khÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng cÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²n ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€¦Ã‚Â¸ trÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¡ng thÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¡i chÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â duyÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¡t." });
        if (ApprovedParticipants(match).Count >= match.RequiredPlayerCount)
            return Conflict(new { message = "PhÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â£ ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â§ sÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ ngÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi cÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â§n thiÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¿t." });
        if (participant.Player.SkillLevel < match.MinSkillLevel || participant.Player.SkillLevel > match.MaxSkillLevel)
            return Conflict(new { message = "TrÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¬nh ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢ ngÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â¡i khÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng cÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²n phÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¹ hÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â£p vÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‚Âºi lÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi mÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi." });

        participant.Status = "Approved";
        participant.RespondedAt = DateTime.UtcNow;
        await AddConversationParticipantAsync(match, participant.Player.UserId, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _matchRealtime.Publish(matchId, "ParticipantApproved");
        return Ok(await LoadOpenMatchResponseAsync(matchId, hostPlayerId, cancellationToken));
    }
    public async Task<ServiceResult<OpenMatchDetailResponse>> RejectParticipant(
        int matchId,
        int participantId,
        CancellationToken cancellationToken)
    {
        var hostPlayerId = await CurrentPlayerIdAsync(cancellationToken);
        if (hostPlayerId is null) return BadRequest(new { message = "TÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â i khoÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â£n chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°a cÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â³ hÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã…â€œ sÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â¡ ngÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â¡i." });
        var match = await MatchInvitationQuery().SingleOrDefaultAsync(item => item.MatchId == matchId, cancellationToken);
        if (match is null) return NotFound(new { message = "KhÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng tÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¬m thÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¥y phÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng ghÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â©p trÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­n." });
        if (match.HostPlayerId != hostPlayerId) return Forbid();
        if (match.Status != "Recruiting")
            return Conflict(new { message = "KhÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng thÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€ Ã¢â‚¬â„¢ xÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â­ lÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â½ yÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Âªu cÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â§u sau khi phÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â£ chuyÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€ Ã¢â‚¬â„¢n sang ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â·t sÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢n." });

        var participant = match.MatchParticipants.SingleOrDefault(item => item.ParticipantId == participantId);
        if (participant is null || participant.Status != "Pending")
            return Conflict(new { message = "YÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Âªu cÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â§u tham gia khÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng cÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²n ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€¦Ã‚Â¸ trÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¡ng thÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¡i chÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â duyÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¡t." });
        participant.Status = "Rejected";
        participant.RespondedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        _matchRealtime.Publish(matchId, "ParticipantRejected");
        return Ok(await LoadOpenMatchResponseAsync(matchId, hostPlayerId, cancellationToken));
    }
    public async Task<ServiceResult<OpenMatchDetailResponse>> RemoveParticipant(
        int matchId,
        int participantId,
        CancellationToken cancellationToken)
    {
        var hostPlayerId = await CurrentPlayerIdAsync(cancellationToken);
        if (hostPlayerId is null) return BadRequest(new { message = "TÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â i khoÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â£n chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°a cÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â³ hÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã…â€œ sÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â¡ ngÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â¡i." });

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_db, transaction, $"match-roster:{matchId}", cancellationToken))
            return Conflict(new { message = "Danh sÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¡ch ngÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â¡i ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ang ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â£c cÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­p nhÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­t." });
        var match = await MatchInvitationQuery().SingleOrDefaultAsync(item => item.MatchId == matchId, cancellationToken);
        if (match is null) return NotFound(new { message = "KhÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng tÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¬m thÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¥y phÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng ghÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â©p trÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­n." });
        if (match.HostPlayerId != hostPlayerId) return Forbid();
        if (match.Status is "BookingPending" or "Booked" or "Completed")
            return Conflict(new { message = "KhÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng thÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€ Ã¢â‚¬â„¢ loÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¡i thÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â nh viÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Âªn sau khi booking ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â£ ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â£c tÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¡o." });

        var participant = match.MatchParticipants.SingleOrDefault(item => item.ParticipantId == participantId);
        if (participant is null || participant.IsHost || !IsApproved(participant))
            return Conflict(new { message = "KhÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng thÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€ Ã¢â‚¬â„¢ loÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¡i thÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â nh viÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Âªn nÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â y." });
        participant.Status = "Removed";
        participant.RespondedAt = DateTime.UtcNow;
        if (match.Status == "ReadyToBook") match.Status = "Recruiting";
        await RemoveConversationParticipantAsync(match, participant.Player.UserId, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _matchRealtime.Publish(matchId, "ParticipantRemoved");
        return Ok(await LoadOpenMatchResponseAsync(matchId, hostPlayerId, cancellationToken));
    }
    public async Task<ServiceResult<OpenMatchDetailResponse>> MarkReadyToBook(
        int matchId,
        CancellationToken cancellationToken)
    {
        var hostPlayerId = await CurrentPlayerIdAsync(cancellationToken);
        if (hostPlayerId is null) return BadRequest(new { message = "TÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â i khoÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â£n chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°a cÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â³ hÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã…â€œ sÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â¡ ngÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â¡i." });
        var match = await MatchInvitationQuery().SingleOrDefaultAsync(item => item.MatchId == matchId, cancellationToken);
        if (match is null) return NotFound(new { message = "KhÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng tÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¬m thÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¥y phÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng ghÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â©p trÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­n." });
        if (match.HostPlayerId != hostPlayerId) return Forbid();
        if (match.Status == "ReadyToBook")
            return Ok(await LoadOpenMatchResponseAsync(matchId, hostPlayerId, cancellationToken));
        if (match.Status != "Recruiting")
            return Conflict(new { message = "PhÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng khÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€¦Ã‚Â¸ trÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¡ng thÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¡i cÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â³ thÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€ Ã¢â‚¬â„¢ chuyÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€ Ã¢â‚¬â„¢n sang ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â·t sÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢n." });
        if (ApprovedParticipants(match).Count != match.RequiredPlayerCount)
            return Conflict(new { message = "PhÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°a ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â§ sÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ thÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â nh viÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Âªn ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â£ ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â£c duyÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¡t." });

        match.Status = "ReadyToBook";
        await _db.SaveChangesAsync(cancellationToken);
        _matchRealtime.Publish(matchId, "ReadyToBook");
        return Ok(await LoadOpenMatchResponseAsync(matchId, hostPlayerId, cancellationToken));
    }
    public async Task<ServiceResult<OpenMatchDetailResponse>> CreateMatchBooking(
        int matchId,
        CreateMatchBookingRequest request,
        CancellationToken cancellationToken)
    {
        var currentPlayerId = await CurrentPlayerIdAsync(cancellationToken);
        if (currentPlayerId is null) return BadRequest(new { message = "TÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â i khoÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â£n chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°a cÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â³ hÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã…â€œ sÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â¡ ngÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â¡i." });
        if (request.StartTime <= DateTime.Now)
            return BadRequest(new { message = "ThÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi gian bÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¯t ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â§u phÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â£i ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€¦Ã‚Â¸ tÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â¡ng lai." });
        if (request.EndTime <= request.StartTime)
            return BadRequest(new { message = "ThÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi gian kÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¿t thÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Âºc phÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â£i sau thÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi gian bÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¯t ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â§u." });
        if (DateOnly.FromDateTime(request.StartTime) != DateOnly.FromDateTime(request.EndTime))
            return BadRequest(new { message = "Booking ghÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â©p trÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­n phÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â£i bÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¯t ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â§u vÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â  kÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¿t thÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Âºc trong cÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¹ng mÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢t ngÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â y." });

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_db, transaction, $"match-roster:{matchId}", cancellationToken))
            return Conflict(new { message = "PhÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng ghÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â©p trÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­n ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ang ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â£c cÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­p nhÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­t." });
        if (!await SqlServerBookingLock.AcquireAsync(_db, transaction, $"court-booking:{request.CourtId}", cancellationToken))
            return Conflict(new { message = "SÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢n ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ang ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â£c ngÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi khÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¡c thao tÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¡c. Vui lÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng thÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â­ lÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¡i." });

        var match = await MatchInvitationQuery().SingleOrDefaultAsync(item => item.MatchId == matchId, cancellationToken);
        if (match is null) return NotFound(new { message = "KhÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng tÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¬m thÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¥y phÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng ghÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â©p trÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­n." });
        if (match.Status != "ReadyToBook")
            return Conflict(new { message = "PhÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng phÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â£i ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€¦Ã‚Â¸ trÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¡ng thÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¡i sÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Âµn sÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â ng ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â·t sÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢n." });
        var approved = ApprovedParticipants(match);
        if (!approved.Any(participant => participant.PlayerId == currentPlayerId.Value)) return Forbid();
        if (approved.Count != match.RequiredPlayerCount)
            return Conflict(new { message = "Danh sÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¡ch thÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â nh viÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Âªn khÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng cÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²n ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â§ ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€ Ã¢â‚¬â„¢ tÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¡o booking." });
        if (match.Bookings.Any(booking => !InactiveBookingStatuses.Contains(booking.Status)))
            return Conflict(new { message = "PhÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â£ cÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â³ mÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢t booking ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ang hoÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¡t ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢ng." });

        var bookingDate = DateOnly.FromDateTime(request.StartTime);
        var bookingTimeStart = TimeOnly.FromDateTime(request.StartTime);
        var bookingTimeEnd = TimeOnly.FromDateTime(request.EndTime);
        if (!match.AvailableDateFrom.HasValue
            || !match.AvailableDateTo.HasValue
            || bookingDate < match.AvailableDateFrom.Value
            || bookingDate > match.AvailableDateTo.Value)
            return BadRequest(new { message = "NgÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â y ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â·t sÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢n phÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â£i nÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â±m trong khoÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â£ng ngÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â y cÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â³ thÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€ Ã¢â‚¬â„¢ chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â¡i ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â£ khai bÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¡o." });
        if (match.AvailabilitySlots.Count > 0)
        {
            var fitsDeclaredSlot = match.AvailabilitySlots.Any(slot =>
                bookingTimeStart >= slot.TimeStart
                && bookingTimeEnd <= slot.TimeEnd);
            if (!fitsDeclaredSlot)
                return BadRequest(new { message = "Khung giÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â·t sÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢n phÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â£i nÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â±m trÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Ân trong mÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢t slot ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â£ khai bÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¡o." });
        }
        else
        {
            if (!match.PreferredTimeStart.HasValue
                || !match.PreferredTimeEnd.HasValue
                || bookingTimeStart < match.PreferredTimeStart.Value
                || bookingTimeEnd > match.PreferredTimeEnd.Value)
                return BadRequest(new { message = "Khung giÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â·t sÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢n phÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â£i nÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â±m trong khoÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â£ng giÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â mong muÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“n ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â£ khai bÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¡o." });
        }

        var court = await _db.Courts
            .Include(item => item.Venue).ThenInclude(item => item.BookingRules)
            .SingleOrDefaultAsync(item => item.CourtId == request.CourtId, cancellationToken);
        if (court is null) return NotFound(new { message = "KhÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng tÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¬m thÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¥y sÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢n con." });
        if (!court.Venue.IsOpen || court.AvailabilityStatus != "Available")
            return Conflict(new { message = "SÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢n hiÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¡n khÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng nhÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­n ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â·t lÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¹ch." });
        if (!PreferredVenueIds(match).Contains(court.VenueId))
            return BadRequest(new { message = "ChÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â° ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â£c chÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Ân cÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â¥m sÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢n trong danh sÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¡ch mong muÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“n cÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â§a phÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng." });
        if (request.StartTime < bookingDate.ToDateTime(court.Venue.OpenTime)
            || request.EndTime > bookingDate.ToDateTime(court.Venue.CloseTime))
            return BadRequest(new { message = $"Khung giÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â phÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â£i nÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â±m trong giÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â mÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€¦Ã‚Â¸ cÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â­a {court.Venue.OpenTime:HH:mm}ÃƒÆ’Ã‚Â¢ÃƒÂ¢Ã¢â‚¬Å¡Ã‚Â¬ÃƒÂ¢Ã¢â€šÂ¬Ã…â€œ{court.Venue.CloseTime:HH:mm}." });

        var now = DateTime.UtcNow;
        var overlaps = await _db.Bookings.AnyAsync(booking =>
            booking.CourtId == request.CourtId
            && !InactiveBookingStatuses.Contains(booking.Status)
            && (booking.Status != "Holding" || booking.HoldExpiresAt > now)
            && booking.StartTime < request.EndTime
            && booking.EndTime > request.StartTime,
            cancellationToken);
        if (overlaps) return Conflict(new { message = "Khung giÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â nÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â y vÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â«a ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â£c giÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â¯ hoÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â·c ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â£ ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â£c ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â·t." });

        foreach (var participant in approved.OrderBy(item => item.PlayerId))
        {
            if (!await SqlServerBookingLock.AcquireAsync(_db, transaction, $"player-schedule:{participant.PlayerId}", cancellationToken))
                return Conflict(new { message = "LÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¹ch cÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â§a mÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢t thÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â nh viÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Âªn ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ang ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â£c cÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­p nhÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­t. Vui lÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng thÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â­ lÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¡i." });
            if (await _playerScheduleConflict.HasConflictAsync(
                    participant.PlayerId,
                    request.StartTime,
                    request.EndTime,
                    excludedMatchId: match.MatchId,
                    cancellationToken: cancellationToken))
                return Conflict(new { message = $"{participant.Player.User.Username} ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â£ cÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â³ lÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â¹ch trÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¹ng vÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‚Âºi khung giÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â£c chÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Ân." });
        }

        var hourlyPrice = court.HourlyPrice > 0 ? court.HourlyPrice : MatchVenueBasePrice(court.Venue);
        if (hourlyPrice <= 0)
            return Conflict(new { message = "SÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢n chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°a ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â£c thiÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¿t lÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­p giÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¡ theo giÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â." });
        var totalAmount = Math.Round(
            hourlyPrice * (request.EndTime - request.StartTime).TotalHours,
            0,
            MidpointRounding.AwayFromZero);
        var booking = new Booking
        {
            PlayerId = currentPlayerId.Value,
            CourtId = court.CourtId,
            Court = court,
            Match = match,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            Status = "Holding",
            Title = match.Title,
            BookingCode = $"PM-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            CreatedAt = now,
            HoldExpiresAt = now.AddMinutes(Math.Clamp(_configuration.GetValue("Match:PaymentMinutes", 5), 1, 1440)),
            HourlyPriceSnapshot = hourlyPrice,
            CourtAmount = totalAmount,
            TotalAmount = totalAmount
        };
        var bookingActor = match.HostPlayerId == currentPlayerId.Value ? "ChÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â§ phÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng" : "ThÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â nh viÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Âªn";
        booking.StatusHistories.Add(NewMatchBookingHistory(
            null,
            "Holding",
            $"{bookingActor} tÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¡o booking sau khi ghÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â©p ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â§ ngÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi",
            CurrentUserId()));
        match.MatchTime = request.StartTime;
        match.Status = "BookingPending";
        _db.Bookings.Add(booking);
        await CreateSplitPaymentsAsync(match, booking, approved, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _scheduleRealtime.Publish(new ScheduleChangedEvent(
            court.VenueId, court.CourtId, booking.StartTime, booking.EndTime, booking.Status, "Created"));
        _matchRealtime.Publish(matchId, "BookingCreated");
        return Ok(await LoadOpenMatchResponseAsync(matchId, currentPlayerId, cancellationToken));
    }
    public async Task<ServiceResult<List<MatchSlotOptionResponse>>> GetMatchSlotOptions(
        int matchId,
        int venueId,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var context = await EnsureApprovedParticipantAsync(matchId, cancellationToken);
        if (context is null) return Forbid();
        if (context.Value.Match.Status != "ReadyToBook")
            return Conflict(new { message = "PhÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng phÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â£i sÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Âµn sÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â ng ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â·t sÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢n trÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‚Âºc khi chÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Ân slot chung." });
        if (!PreferredVenueIds(context.Value.Match).Contains(venueId))
            return BadRequest(new { message = "CÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â¥m sÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢n khÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng thuÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢c danh sÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¡ch mong muÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“n cÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â§a phÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng." });

        return Ok(await BuildMatchSlotOptionsAsync(
            context.Value.Match,
            context.Value.PlayerId,
            venueId,
            date,
            cancellationToken));
    }
    public async Task<ServiceResult<List<MatchSlotOptionResponse>>> VoteMatchSlot(
        int matchId,
        MatchSlotVoteRequest request,
        CancellationToken cancellationToken)
    {
        var context = await EnsureApprovedParticipantAsync(matchId, cancellationToken);
        if (context is null) return Forbid();
        if (context.Value.Match.Status != "ReadyToBook")
            return Conflict(new { message = "PhÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng phÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â£i sÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Âµn sÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â ng ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â·t sÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢n trÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‚Âºc khi vote slot chung." });
        var date = DateOnly.FromDateTime(request.StartTime);
        var court = await _db.Courts.AsNoTracking()
            .SingleOrDefaultAsync(item => item.CourtId == request.CourtId, cancellationToken);
        if (court is null) return NotFound(new { message = "KhÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng tÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¬m thÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¥y sÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢n con." });
        if (!PreferredVenueIds(context.Value.Match).Contains(court.VenueId))
            return BadRequest(new { message = "CÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â¥m sÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢n khÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng thuÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â‚¬Å¾Ã‚Â¢c danh sÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¡ch mong muÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“n cÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â§a phÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng." });

        var options = await BuildMatchSlotOptionsAsync(
            context.Value.Match,
            context.Value.PlayerId,
            court.VenueId,
            date,
            cancellationToken);
        var option = options.SingleOrDefault(item =>
            item.CourtId == request.CourtId
            && item.StartTime == request.StartTime
            && item.EndTime == request.EndTime);
        if (option is null || !option.IsCompatibleForAll)
            return Conflict(new { message = "Slot nÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â y khÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng cÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²n rÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â£nh cho tÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¥t cÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â£ thÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â nh viÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Âªn." });

        var exists = await _db.MatchSlotVotes.AnyAsync(item =>
            item.MatchId == matchId
            && item.PlayerId == context.Value.PlayerId
            && item.CourtId == request.CourtId
            && item.StartTime == request.StartTime
            && item.EndTime == request.EndTime,
            cancellationToken);
        if (!exists)
        {
            _db.MatchSlotVotes.Add(new MatchSlotVote
            {
                MatchId = matchId,
                PlayerId = context.Value.PlayerId,
                CourtId = request.CourtId,
                StartTime = request.StartTime,
                EndTime = request.EndTime,
                CreatedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(cancellationToken);
            _matchRealtime.Publish(matchId, "SlotVoteChanged");
        }

        return Ok(await BuildMatchSlotOptionsAsync(
            context.Value.Match,
            context.Value.PlayerId,
            court.VenueId,
            date,
            cancellationToken));
    }
    public async Task<ServiceResult<List<MatchSlotOptionResponse>>> UnvoteMatchSlot(
        int matchId,
        MatchSlotVoteRequest request,
        CancellationToken cancellationToken)
    {
        var context = await EnsureApprovedParticipantAsync(matchId, cancellationToken);
        if (context is null) return Forbid();
        var date = DateOnly.FromDateTime(request.StartTime);
        var court = await _db.Courts.AsNoTracking()
            .SingleOrDefaultAsync(item => item.CourtId == request.CourtId, cancellationToken);
        if (court is null) return NotFound(new { message = "KhÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng tÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¬m thÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¥y sÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢n con." });

        var vote = await _db.MatchSlotVotes.SingleOrDefaultAsync(item =>
            item.MatchId == matchId
            && item.PlayerId == context.Value.PlayerId
            && item.CourtId == request.CourtId
            && item.StartTime == request.StartTime
            && item.EndTime == request.EndTime,
            cancellationToken);
        if (vote is not null)
        {
            _db.MatchSlotVotes.Remove(vote);
            await _db.SaveChangesAsync(cancellationToken);
            _matchRealtime.Publish(matchId, "SlotVoteChanged");
        }

        return Ok(await BuildMatchSlotOptionsAsync(
            context.Value.Match,
            context.Value.PlayerId,
            court.VenueId,
            date,
            cancellationToken));
    }
    public async Task<ServiceResult<OpenMatchDetailResponse>> CancelOpenMatch(
        int matchId,
        CancellationToken cancellationToken)
    {
        var hostPlayerId = await CurrentPlayerIdAsync(cancellationToken);
        if (hostPlayerId is null) return BadRequest(new { message = "TÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â i khoÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â£n chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°a cÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â³ hÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã…â€œ sÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â¡ ngÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â¡i." });

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_db, transaction, $"match-roster:{matchId}", cancellationToken))
            return Conflict(new { message = "PhÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ang ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â£c cÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­p nhÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­t." });
        var match = await MatchInvitationQuery().SingleOrDefaultAsync(item => item.MatchId == matchId, cancellationToken);
        if (match is null) return NotFound(new { message = "KhÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng tÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¬m thÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¥y phÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng ghÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â©p trÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­n." });
        if (match.HostPlayerId != hostPlayerId) return Forbid();
        if (match.Status == "Completed")
            return Conflict(new { message = "KhÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng thÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€ Ã¢â‚¬â„¢ hÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â§y trÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­n ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â£ hoÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â n thÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â nh." });
        if (match.Status == "Cancelled")
            return Ok(await LoadOpenMatchResponseAsync(matchId, hostPlayerId, cancellationToken));

        var booking = CurrentBooking(match);
        match.Status = "Cancelled";
        match.CancelledAt = DateTime.UtcNow;
        if (booking is not null && !InactiveBookingStatuses.Contains(booking.Status))
        {
            var oldBookingStatus = booking.Status;
            booking.Status = "Cancelled";
            booking.HoldExpiresAt = null;
            booking.StatusHistories.Add(NewMatchBookingHistory(
                oldBookingStatus,
                "Cancelled",
                "ChÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â§ phÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng hÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â§y trÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­n",
                CurrentUserId()));
            foreach (var payment in booking.Payments.Where(item => item.Status is not "Cancelled" and not "Refunded"))
            {
                var previous = payment.Status;
                payment.Status = payment.Status == "Paid" ? "RefundPending" : "Cancelled";
                payment.StatusHistories.Add(NewMatchPaymentHistory(
                    previous,
                    payment.Status,
                    "MatchCancelled",
                    "ChÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â§ phÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng hÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â§y trÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­n",
                    CurrentUserId()));
            }
        }
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (booking is not null)
            _scheduleRealtime.Publish(new ScheduleChangedEvent(
                booking.Court.VenueId, booking.CourtId, booking.StartTime, booking.EndTime, "Cancelled", "Deleted"));
        _matchRealtime.Publish(matchId, "Cancelled");
        return Ok(await LoadOpenMatchResponseAsync(matchId, hostPlayerId, cancellationToken));
    }
    public async Task<ServiceResult<OpenMatchDetailResponse>> ReopenMatch(
        int matchId,
        CancellationToken cancellationToken)
    {
        var hostPlayerId = await CurrentPlayerIdAsync(cancellationToken);
        if (hostPlayerId is null) return BadRequest(new { message = "TÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â i khoÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â£n chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°a cÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â³ hÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã…â€œ sÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â¡ ngÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â¡i." });
        var match = await MatchInvitationQuery().SingleOrDefaultAsync(item => item.MatchId == matchId, cancellationToken);
        if (match is null) return NotFound(new { message = "KhÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng tÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¬m thÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¥y phÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng ghÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â©p trÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­n." });
        if (match.HostPlayerId != hostPlayerId) return Forbid();
        if (match.Status != "Cancelled")
            return Conflict(new { message = "ChÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â° cÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â³ thÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€ Ã¢â‚¬â„¢ mÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€¦Ã‚Â¸ lÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¡i lÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi mÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â£ hÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â§y." });
        if (!match.AvailableDateTo.HasValue || match.AvailableDateTo < DateOnly.FromDateTime(DateTime.Today))
            return Conflict(new { message = "KhoÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â£ng ngÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â y cÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â³ thÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€ Ã¢â‚¬â„¢ chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â¡i ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â£ kÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¿t thÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Âºc." });
        if (match.Bookings.SelectMany(item => item.Payments).Any(item => item.Status is "Paid" or "RefundPending"))
            return Conflict(new { message = "CÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â§n hoÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â n tÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¥t hoÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â n tiÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Ân trÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‚Âºc khi mÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€¦Ã‚Â¸ lÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¡i phÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng." });

        match.Status = ApprovedParticipants(match).Count == match.RequiredPlayerCount
            ? "ReadyToBook"
            : "Recruiting";
        match.CancelledAt = null;
        await _db.SaveChangesAsync(cancellationToken);
        _matchRealtime.Publish(matchId, "Reopened");
        return Ok(await LoadOpenMatchResponseAsync(matchId, hostPlayerId, cancellationToken));
    }
    public async Task<ServiceResult<OpenMatchDetailResponse>> CompleteOpenMatch(
        int matchId,
        CancellationToken cancellationToken)
    {
        var playerId = await CurrentPlayerIdAsync(cancellationToken);
        if (playerId is null) return BadRequest(new { message = "TÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â i khoÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â£n chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°a cÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â³ hÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã…â€œ sÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â¡ ngÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â¡i." });
        var match = await MatchInvitationQuery().SingleOrDefaultAsync(item => item.MatchId == matchId, cancellationToken);
        if (match is null) return NotFound(new { message = "KhÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng tÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¬m thÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¥y phÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng ghÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â©p trÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­n." });
        if (match.HostPlayerId != playerId) return Forbid();
        if (match.Status != "Booked")
            return Conflict(new { message = "ChÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â° cÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â³ thÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€ Ã¢â‚¬â„¢ hoÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â n thÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â nh trÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­n ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â£ ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â·t sÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢n thÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â nh cÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng." });
        var booking = CurrentBooking(match);
        if (booking is null || booking.EndTime > DateTime.Now)
            return Conflict(new { message = "TrÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­n chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°a kÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¿t thÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Âºc." });

        match.Status = "Completed";
        var oldBookingStatus = booking.Status;
        booking.Status = "Completed";
        booking.StatusHistories.Add(NewMatchBookingHistory(
            oldBookingStatus,
            "Completed",
            "ChÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â§ phÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng xÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¡c nhÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­n hoÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â n thÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â nh",
            CurrentUserId()));
        await _db.SaveChangesAsync(cancellationToken);
        _matchRealtime.Publish(matchId, "Completed");
        return Ok(await LoadOpenMatchResponseAsync(matchId, playerId, cancellationToken));
    }
    public async Task<ServiceResult<MatchPlayerReviewResponse>> ReviewMatchPlayer(
        int matchId,
        int revieweePlayerId,
        CreateMatchPlayerReviewRequest request,
        CancellationToken cancellationToken)
    {
        var reviewer = await CurrentPlayerAsync(cancellationToken);
        if (reviewer is null) return BadRequest(new { message = "TÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â i khoÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â£n chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°a cÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â³ hÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã…â€œ sÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â¡ ngÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â¡i." });
        if (reviewer.PlayerId == revieweePlayerId)
            return BadRequest(new { message = "BÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¡n khÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng thÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€ Ã¢â‚¬â„¢ tÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â± ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¡nh giÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¡ chÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â­nh mÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¬nh." });

        var match = await _db.Matches
            .Include(item => item.MatchParticipants).ThenInclude(item => item.Player).ThenInclude(item => item.User)
            .SingleOrDefaultAsync(item => item.MatchId == matchId, cancellationToken);
        if (match is null) return NotFound(new { message = "KhÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â´ng tÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¬m thÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¥y trÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­n." });
        if (match.Status != "Completed")
            return Conflict(new { message = "ChÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã‚Â° ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â£c ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¡nh giÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¡ ngÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â¡i sau khi trÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­n hoÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â n thÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â nh." });
        if (!match.MatchParticipants.Any(item => item.PlayerId == reviewer.PlayerId && IsApproved(item))
            || !match.MatchParticipants.Any(item => item.PlayerId == revieweePlayerId && IsApproved(item)))
            return Forbid();
        if (await _db.MatchPlayerReviews.AnyAsync(item =>
            item.MatchId == matchId
            && item.ReviewerPlayerId == reviewer.PlayerId
            && item.RevieweePlayerId == revieweePlayerId,
            cancellationToken))
            return Conflict(new { message = "BÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¡n ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â£ ÃƒÆ’Ã¢â‚¬Å¾ÃƒÂ¢Ã¢â€šÂ¬Ã‹Å“ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¡nh giÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¡ ngÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â¡i nÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â y trong trÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â­n." });

        var review = new MatchPlayerReview
        {
            MatchId = matchId,
            ReviewerPlayerId = reviewer.PlayerId,
            RevieweePlayerId = revieweePlayerId,
            Score = request.Score,
            Comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim(),
            CreatedAt = DateTime.UtcNow
        };
        _db.MatchPlayerReviews.Add(review);
        await _db.SaveChangesAsync(cancellationToken);
        _matchRealtime.Publish(matchId, "PlayerReviewed");
        var reviewee = match.MatchParticipants.Single(item => item.PlayerId == revieweePlayerId).Player;
        return Ok(new MatchPlayerReviewResponse
        {
            MatchPlayerReviewId = review.MatchPlayerReviewId,
            MatchId = matchId,
            ReviewerPlayerId = reviewer.PlayerId,
            ReviewerName = reviewer.User.Username,
            RevieweePlayerId = revieweePlayerId,
            RevieweeName = reviewee.User.Username,
            Score = review.Score,
            Comment = review.Comment,
            CreatedAt = review.CreatedAt
        });
    }
    public async Task<ServiceResult<List<MatchPlayerReviewResponse>>> GetMatchPlayerReviews(
        int matchId,
        CancellationToken cancellationToken)
    {
        var playerId = await CurrentPlayerIdAsync(cancellationToken);
        if (playerId is null) return BadRequest(new { message = "TÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â i khoÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â£n chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°a cÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â³ hÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»ÃƒÂ¢Ã¢â€šÂ¬Ã…â€œ sÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â¡ ngÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â°ÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Âi chÃƒÆ’Ã¢â‚¬Â Ãƒâ€šÃ‚Â¡i." });
        var isParticipant = await _db.MatchParticipants.AnyAsync(item =>
            item.MatchId == matchId
            && item.PlayerId == playerId
            && (item.Status == "Approved" || item.Status == "Accepted"),
            cancellationToken);
        if (!isParticipant) return Forbid();

        var reviews = await _db.MatchPlayerReviews.AsNoTracking()
            .Where(item => item.MatchId == matchId)
            .Include(item => item.ReviewerPlayer).ThenInclude(item => item.User)
            .Include(item => item.RevieweePlayer).ThenInclude(item => item.User)
            .OrderBy(item => item.CreatedAt)
            .Select(item => new MatchPlayerReviewResponse
            {
                MatchPlayerReviewId = item.MatchPlayerReviewId,
                MatchId = item.MatchId,
                ReviewerPlayerId = item.ReviewerPlayerId,
                ReviewerName = item.ReviewerPlayer.User.Username,
                RevieweePlayerId = item.RevieweePlayerId,
                RevieweeName = item.RevieweePlayer.User.Username,
                Score = item.Score,
                Comment = item.Comment,
                CreatedAt = item.CreatedAt
            })
            .ToListAsync(cancellationToken);
        return Ok(reviews);
    }

    private IQueryable<Match> MatchInvitationQuery(bool asNoTracking = false)
    {
        IQueryable<Match> query = _db.Matches
            .AsSplitQuery()
            .Include(item => item.HostPlayer).ThenInclude(item => item!.User)
            .Include(item => item.AvailabilitySlots)
            .Include(item => item.MatchParticipants).ThenInclude(item => item.Player).ThenInclude(item => item.User)
            .Include(item => item.MatchCheckIns)
            .Include(item => item.Conversations).ThenInclude(item => item.ConversationParticipants)
            .Include(item => item.Bookings).ThenInclude(item => item.Court).ThenInclude(item => item.Venue).ThenInclude(item => item.BookingRules)
            .Include(item => item.Bookings).ThenInclude(item => item.Payments).ThenInclude(item => item.StatusHistories);
        return asNoTracking ? query.AsNoTracking() : query;
    }

    private IQueryable<Match> MatchSearchQuery(bool asNoTracking = false)
    {
        IQueryable<Match> query = _db.Matches
            .AsSplitQuery()
            .Include(item => item.HostPlayer).ThenInclude(item => item!.User)
            .Include(item => item.AvailabilitySlots)
            .Include(item => item.MatchParticipants);
        return asNoTracking ? query.AsNoTracking() : query;
    }

    private IQueryable<Match> MyMatchesQuery(bool asNoTracking = false)
    {
        IQueryable<Match> query = _db.Matches
            .AsSingleQuery()
            .Include(item => item.MatchParticipants)
            .Include(item => item.Bookings.Where(booking =>
                booking.Status != "Cancelled" && booking.Status != "Expired"))
                .ThenInclude(item => item.Court)
                .ThenInclude(item => item.Venue);
        return asNoTracking ? query.AsNoTracking() : query;
    }

    private async Task<(Match Match, int PlayerId)?> EnsureApprovedParticipantAsync(
        int matchId,
        CancellationToken cancellationToken)
    {
        var playerId = await CurrentPlayerIdAsync(cancellationToken);
        if (playerId is null) return null;

        var match = await MatchInvitationQuery(asNoTracking: true)
            .SingleOrDefaultAsync(item => item.MatchId == matchId, cancellationToken);
        if (match is null) return null;
        return ApprovedParticipants(match).Any(participant => participant.PlayerId == playerId.Value)
            ? (match, playerId.Value)
            : null;
    }

    private async Task<List<MatchSlotOptionResponse>> BuildMatchSlotOptionsAsync(
        Match match,
        int currentPlayerId,
        int venueId,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var approved = ApprovedParticipants(match);
        var participantCount = approved.Count;
        var venue = await _db.Venues.AsNoTracking()
            .Include(item => item.Courts)
            .SingleOrDefaultAsync(
                venue => venue.VenueId == venueId
                    && venue.ApprovalStatus == "Approved"
                    && venue.IsOpen,
                cancellationToken);
        if (venue is null) return [];

        if (!match.AvailableDateFrom.HasValue
            || !match.AvailableDateTo.HasValue
            || date < match.AvailableDateFrom.Value
            || date > match.AvailableDateTo.Value)
        {
            return [];
        }

        var dayStart = date.ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);
        var now = DateTime.UtcNow;
        var bookings = await _db.Bookings.AsNoTracking()
            .Where(booking =>
                booking.Court.VenueId == venueId
                && booking.StartTime < dayEnd
                && booking.EndTime > dayStart
                && !InactiveBookingStatuses.Contains(booking.Status)
                && (booking.Status != "Holding" || booking.HoldExpiresAt > now))
            .ToListAsync(cancellationToken);
        await EnsureMatchSlotVoteSchemaAsync(cancellationToken);
        var votes = await _db.MatchSlotVotes.AsNoTracking()
            .Include(item => item.Player).ThenInclude(item => item.User)
            .Where(item =>
                item.MatchId == match.MatchId
                && item.StartTime < dayEnd
                && item.EndTime > dayStart)
            .ToListAsync(cancellationToken);

        var result = new List<MatchSlotOptionResponse>();
        foreach (var court in venue.Courts.Where(item => item.AvailabilityStatus != "Inactive").OrderBy(item => item.CourtNumber))
        {
            var opening = date.ToDateTime(venue.OpenTime);
            var closing = date.ToDateTime(venue.CloseTime);
            for (var start = opening; start.AddMinutes(30) <= closing; start = start.AddMinutes(30))
            {
                var end = start.AddMinutes(30);
                var overlap = bookings.FirstOrDefault(booking =>
                    booking.CourtId == court.CourtId
                    && booking.StartTime < end
                    && booking.EndTime > start);
                var status = !venue.IsOpen ? "Closed"
                    : court.AvailabilityStatus == "Maintenance" ? "Maintenance"
                    : overlap is null ? "Available"
                    : overlap.Status == "Holding" ? "Holding"
                    : overlap.PlayerId is not null ? "Booked"
                    : overlap.OwnerEntryType ?? "Blocked";
                if (!SlotFitsMatch(match, date, start, end)) continue;

                var participantConflicts = 0;
                if (status == "Available" && start > DateTime.Now)
                {
                    foreach (var participant in approved)
                    {
                        if (await _playerScheduleConflict.HasConflictAsync(
                                participant.PlayerId,
                                start,
                                end,
                                excludedMatchId: match.MatchId,
                                cancellationToken: cancellationToken))
                        {
                            participantConflicts += 1;
                        }
                    }
                }
                else
                {
                    participantConflicts = participantCount;
                }

                var slotVotes = votes
                    .Where(vote =>
                        vote.CourtId == court.CourtId
                        && vote.StartTime == start
                        && vote.EndTime == end)
                    .OrderBy(vote => vote.CreatedAt)
                    .ToList();
                result.Add(new MatchSlotOptionResponse
                {
                    CourtId = court.CourtId,
                    CourtNumber = court.CourtNumber,
                    StartTime = start,
                    EndTime = end,
                    Status = status,
                    IsCompatibleForAll = status == "Available" && start > DateTime.Now && participantConflicts == 0,
                    CompatiblePlayerCount = Math.Max(participantCount - participantConflicts, 0),
                    RequiredPlayerCount = participantCount,
                    VoteCount = slotVotes.Select(vote => vote.PlayerId).Distinct().Count(),
                    VoterNames = slotVotes
                        .GroupBy(vote => vote.PlayerId)
                        .Select(group => group.First().Player.User.Username)
                        .ToList(),
                    IsVotedByMe = slotVotes.Any(vote => vote.PlayerId == currentPlayerId)
                });
            }
        }

        return result;
    }

    private async Task EnsureMatchSlotVoteSchemaAsync(CancellationToken cancellationToken)
    {
        await _db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'[MATCH_SLOT_VOTE]', N'U') IS NULL
            BEGIN
                CREATE TABLE [MATCH_SLOT_VOTE] (
                    [matchSlotVoteId] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_MATCH_SLOT_VOTE] PRIMARY KEY,
                    [matchId] int NOT NULL,
                    [playerId] int NOT NULL,
                    [courtId] int NOT NULL,
                    [startTime] datetime NOT NULL,
                    [endTime] datetime NOT NULL,
                    [createdAt] datetime NOT NULL CONSTRAINT [DF_MATCH_SLOT_VOTE_createdAt] DEFAULT (getutcdate()),
                    CONSTRAINT [FK_MATCH_SLOT_VOTE_MATCH] FOREIGN KEY ([matchId]) REFERENCES [MATCH]([matchId]) ON DELETE CASCADE,
                    CONSTRAINT [FK_MATCH_SLOT_VOTE_PLAYER] FOREIGN KEY ([playerId]) REFERENCES [PLAYER]([playerId]) ON DELETE CASCADE,
                    CONSTRAINT [FK_MATCH_SLOT_VOTE_COURT] FOREIGN KEY ([courtId]) REFERENCES [COURT]([courtId]) ON DELETE NO ACTION,
                    CONSTRAINT [CK_MATCH_SLOT_VOTE_time] CHECK ([endTime] > [startTime])
                );
                CREATE INDEX [IX_MATCH_SLOT_VOTE_matchId]
                    ON [MATCH_SLOT_VOTE] ([matchId]);
                CREATE INDEX [IX_MATCH_SLOT_VOTE_court_time]
                    ON [MATCH_SLOT_VOTE] ([courtId], [startTime], [endTime]);
                CREATE UNIQUE INDEX [UQ_MATCH_SLOT_VOTE_player_slot]
                    ON [MATCH_SLOT_VOTE] ([matchId], [playerId], [courtId], [startTime], [endTime]);
            END
            """, cancellationToken);
    }

    private static bool SlotFitsMatch(Match match, DateOnly date, DateTime start, DateTime end)
    {
        if (!match.AvailableDateFrom.HasValue || !match.AvailableDateTo.HasValue) return false;
        if (date < match.AvailableDateFrom.Value || date > match.AvailableDateTo.Value) return false;

        var slotStart = TimeOnly.FromDateTime(start);
        var slotEnd = TimeOnly.FromDateTime(end);
        if (match.AvailabilitySlots.Count > 0)
        {
            return match.AvailabilitySlots.Any(item =>
                slotStart >= item.TimeStart
                && slotEnd <= item.TimeEnd);
        }

        return match.PreferredTimeStart.HasValue
            && match.PreferredTimeEnd.HasValue
            && slotStart >= match.PreferredTimeStart.Value
            && slotEnd <= match.PreferredTimeEnd.Value;
    }

    private async Task<OpenMatchDetailResponse?> LoadOpenMatchResponseAsync(
        int matchId,
        int? currentPlayerId,
        CancellationToken cancellationToken)
    {
        var match = await MatchInvitationQuery(asNoTracking: true)
            .SingleOrDefaultAsync(item => item.MatchId == matchId, cancellationToken);
        if (match is null) return null;
        var venueLookup = await LoadPreferredVenueLookupAsync([match], cancellationToken);
        var result = new OpenMatchDetailResponse();
        CopySearchResponse(MapSearchResponse(match, currentPlayerId, venueLookup), result);
        var booking = CurrentBooking(match);
        var myPayment = currentPlayerId.HasValue && booking is not null
            ? booking.Payments.Where(item => item.PayerId == currentPlayerId.Value)
                .OrderByDescending(item => item.PaymentId)
                .FirstOrDefault()
            : null;
        var isApprovedParticipant = currentPlayerId.HasValue
            && match.MatchParticipants.Any(item => item.PlayerId == currentPlayerId.Value && IsApproved(item));
        result.BookingId = booking?.BookingId;
        result.ConversationId = isApprovedParticipant
            ? match.Conversations.FirstOrDefault(item => item.ConversationType == "LobbyChat")?.ConversationId
            : null;
        result.MyPlayerId = currentPlayerId;
        result.CheckInCode = isApprovedParticipant
            && booking?.Status is "Confirmed" or "Completed"
            ? booking.BookingCode
            : null;
        result.PaymentDeadline = AsUtc(booking?.HoldExpiresAt);
        result.MyPaymentId = myPayment?.PaymentId;
        result.MyQrImageUrl = myPayment?.QrImageUrl;
        result.MyTransferContent = myPayment?.TransferContent;
        result.MyPaymentRejectionReason = myPayment?.RejectionReason;
        result.Participants = match.MatchParticipants
            .OrderByDescending(item => item.IsHost)
            .ThenBy(item => item.RequestedAt)
            .Select(item =>
            {
                var participantPayment = booking?.Payments
                    .Where(payment => payment.PayerId == item.PlayerId)
                    .OrderByDescending(payment => payment.PaymentId)
                    .FirstOrDefault();

                return new MatchParticipantResponse
                {
                    ParticipantId = item.ParticipantId,
                    PlayerId = item.PlayerId,
                    PlayerName = item.Player.User.Username,
                    AvatarUrl = item.Player.User.ProfileImageUrl,
                    SkillLevel = item.Player.SkillLevel,
                    Status = item.Status,
                    IsHost = item.IsHost,
                    RequestedAt = AsUtc(item.RequestedAt),
                    RespondedAt = AsUtc(item.RespondedAt),
                    PaymentId = isApprovedParticipant ? participantPayment?.PaymentId : null,
                    PaymentStatus = isApprovedParticipant ? participantPayment?.Status : null,
                    QrImageUrl = isApprovedParticipant ? participantPayment?.QrImageUrl : null,
                    TransferContent = isApprovedParticipant ? participantPayment?.TransferContent : null,
                    PaymentRejectionReason = isApprovedParticipant ? participantPayment?.RejectionReason : null,
                    CheckInStatus = match.MatchCheckIns
                        .Where(checkIn => checkIn.PlayerId == item.PlayerId)
                        .OrderByDescending(checkIn => checkIn.CheckedInAt)
                        .Select(checkIn => checkIn.Status)
                        .FirstOrDefault() ?? "Pending",
                    CheckedInAt = AsUtc(match.MatchCheckIns
                        .Where(checkIn => checkIn.PlayerId == item.PlayerId)
                        .OrderByDescending(checkIn => checkIn.CheckedInAt)
                        .Select(checkIn => (DateTime?)checkIn.CheckedInAt)
                        .FirstOrDefault())
                };
            })
            .ToList();
        return result;
    }

    private static MatchSearchResponse MapSearchResponse(
        Match match,
        int? currentPlayerId,
        IReadOnlyDictionary<int, MatchPreferredVenueResponse> venueLookup)
    {
        var booking = CurrentBooking(match);
        var approvedCount = ApprovedParticipants(match).Count;
        var myParticipant = currentPlayerId.HasValue
            ? match.MatchParticipants.SingleOrDefault(item => item.PlayerId == currentPlayerId.Value)
            : null;
        var myPayment = currentPlayerId.HasValue && booking is not null
            ? booking.Payments.Where(item => item.PayerId == currentPlayerId.Value)
                .OrderByDescending(item => item.PaymentId)
                .FirstOrDefault()
            : null;
        var preferredVenues = PreferredVenueIds(match)
            .Where(venueLookup.ContainsKey)
            .Select(id => venueLookup[id])
            .ToList();
        return new MatchSearchResponse
        {
            MatchId = match.MatchId,
            HostPlayerId = match.HostPlayerId ?? 0,
            HostName = match.HostPlayer?.User.Username ?? "ChÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â§ phÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng",
            HostAvatarUrl = match.HostPlayer?.User.ProfileImageUrl,
            MatchType = match.MatchType,
            MatchSkillLevel = match.MatchSkillLevel,
            MinSkillLevel = match.MinSkillLevel > 0 ? match.MinSkillLevel : match.MatchSkillLevel,
            MaxSkillLevel = match.MaxSkillLevel > 0 ? match.MaxSkillLevel : match.MatchSkillLevel,
            Status = NormalizeLegacyMatchStatus(match.Status),
            Title = match.Title ?? $"{match.MatchType} tÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¡i {match.Ward}",
            Note = match.Note,
            Province = match.Province ?? string.Empty,
            Ward = match.Ward ?? string.Empty,
            SearchRadiusKm = match.SearchRadiusKm,
            SearchLatitude = match.SearchLatitude,
            SearchLongitude = match.SearchLongitude,
            AvailableDateFrom = match.AvailableDateFrom ?? DateOnly.FromDateTime(match.MatchTime ?? match.CreatedAt),
            AvailableDateTo = match.AvailableDateTo ?? DateOnly.FromDateTime(match.MatchTime ?? match.CreatedAt),
            PreferredTimeStart = (match.PreferredTimeStart ?? TimeOnly.FromDateTime(match.MatchTime ?? match.CreatedAt)).ToString("HH:mm"),
            PreferredTimeEnd = (match.PreferredTimeEnd ?? TimeOnly.FromDateTime((match.MatchTime ?? match.CreatedAt).AddHours(1))).ToString("HH:mm"),
            AvailabilitySlots = match.AvailabilitySlots
                .OrderBy(item => item.TimeStart)
                .Select(item => new MatchAvailabilitySlotResponse
                {
                    MatchAvailabilitySlotId = item.MatchAvailabilitySlotId,
                    TimeStart = item.TimeStart.ToString("HH:mm"),
                    TimeEnd = item.TimeEnd.ToString("HH:mm")
                })
                .ToList(),
            NeededPlayerCount = Math.Max(match.RequiredPlayerCount - 1, 0),
            RequiredPlayerCount = match.RequiredPlayerCount,
            AcceptedPlayerCount = approvedCount,
            PendingRequestCount = match.MatchParticipants.Count(item => item.Status == "Pending"),
            AvailableSlotCount = Math.Max(match.RequiredPlayerCount - approvedCount, 0),
            PreferredVenues = preferredVenues,
            CourtId = booking?.CourtId,
            CourtNumber = booking?.Court.CourtNumber,
            VenueId = booking?.Court.VenueId,
            VenueName = booking?.Court.Venue.VenueName,
            Address = booking?.Court.Venue.Address,
            StartTime = booking?.StartTime,
            EndTime = booking?.EndTime,
            TotalBookingAmount = booking is null ? 0 : EffectiveMatchTotal(booking),
            AmountPerPlayer = booking is null ? 0 : AmountPerPlayer(match, booking),
            IsHost = currentPlayerId.HasValue && match.HostPlayerId == currentPlayerId,
            MyParticipantStatus = myParticipant?.Status,
            MyPaymentStatus = myPayment?.Status
        };
    }

    private static void CopySearchResponse(MatchSearchResponse source, MatchSearchResponse target)
    {
        target.MatchId = source.MatchId;
        target.HostPlayerId = source.HostPlayerId;
        target.HostName = source.HostName;
        target.HostAvatarUrl = source.HostAvatarUrl;
        target.MatchType = source.MatchType;
        target.MatchSkillLevel = source.MatchSkillLevel;
        target.MinSkillLevel = source.MinSkillLevel;
        target.MaxSkillLevel = source.MaxSkillLevel;
        target.Status = source.Status;
        target.Title = source.Title;
        target.Note = source.Note;
        target.Province = source.Province;
        target.Ward = source.Ward;
        target.SearchRadiusKm = source.SearchRadiusKm;
        target.SearchLatitude = source.SearchLatitude;
        target.SearchLongitude = source.SearchLongitude;
        target.AvailableDateFrom = source.AvailableDateFrom;
        target.AvailableDateTo = source.AvailableDateTo;
        target.PreferredTimeStart = source.PreferredTimeStart;
        target.PreferredTimeEnd = source.PreferredTimeEnd;
        target.AvailabilitySlots = source.AvailabilitySlots;
        target.NeededPlayerCount = source.NeededPlayerCount;
        target.RequiredPlayerCount = source.RequiredPlayerCount;
        target.AcceptedPlayerCount = source.AcceptedPlayerCount;
        target.PendingRequestCount = source.PendingRequestCount;
        target.AvailableSlotCount = source.AvailableSlotCount;
        target.PreferredVenues = source.PreferredVenues;
        target.CourtId = source.CourtId;
        target.CourtNumber = source.CourtNumber;
        target.VenueId = source.VenueId;
        target.VenueName = source.VenueName;
        target.Address = source.Address;
        target.StartTime = source.StartTime;
        target.EndTime = source.EndTime;
        target.TotalBookingAmount = source.TotalBookingAmount;
        target.AmountPerPlayer = source.AmountPerPlayer;
        target.IsHost = source.IsHost;
        target.MyParticipantStatus = source.MyParticipantStatus;
        target.MyPaymentStatus = source.MyPaymentStatus;
    }

    private async Task<Dictionary<int, MatchPreferredVenueResponse>> LoadPreferredVenueLookupAsync(
        IEnumerable<Match> matches,
        CancellationToken cancellationToken)
    {
        var ids = matches.SelectMany(PreferredVenueIds).Distinct().ToList();
        if (ids.Count == 0) return [];
        return await _db.Venues.AsNoTracking()
            .Where(venue => ids.Contains(venue.VenueId)
                && venue.ApprovalStatus == "Approved"
                && venue.IsOpen)
            .Select(venue => new MatchPreferredVenueResponse
            {
                VenueId = venue.VenueId,
                VenueName = venue.VenueName,
                Address = venue.Address,
                Latitude = venue.Latitude,
                Longitude = venue.Longitude
            })
            .ToDictionaryAsync(venue => venue.VenueId, cancellationToken);
    }

    private async Task CreateSplitPaymentsAsync(
        Match match,
        Booking booking,
        IReadOnlyCollection<MatchParticipant> approved,
        CancellationToken cancellationToken)
    {
        var account = await _db.OwnerBankAccounts.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OwnerId == booking.Court.Venue.OwnerId && item.IsActive, cancellationToken);
        var amount = AmountPerPlayer(match, booking);
        foreach (var participant in approved)
        {
            var transferContent = $"{booking.BookingCode}-P{participant.PlayerId}";
            var payment = new Payment
            {
                Booking = booking,
                PayerId = participant.PlayerId,
                Amount = amount,
                PaymentMethod = "BankTransfer",
                Status = "Pending",
                TransferCode = $"{booking.BookingCode?.Replace("-", string.Empty)}P{participant.PlayerId}",
                TransferContent = transferContent,
                BankCode = account?.BankCode,
                BankName = account?.BankName,
                BankAccountNumber = account?.AccountNumber,
                BankAccountName = account?.AccountHolderName,
                QrImageUrl = account is null ? null : BuildMatchVietQrUrl(account, amount, transferContent)
            };
            payment.StatusHistories.Add(NewMatchPaymentHistory(
                null,
                "Pending",
                "MatchBookingPaymentCreated",
                "TÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â¡o khoÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚ÂºÃƒâ€šÃ‚Â£n thanh toÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¡n sau khi chÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Â§ phÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â²ng chÃƒÆ’Ã‚Â¡Ãƒâ€šÃ‚Â»Ãƒâ€šÃ‚Ân sÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¢n",
                CurrentUserId()));
            booking.Payments.Add(payment);
        }
    }

    private async Task AddConversationParticipantAsync(
        Match match,
        int userId,
        CancellationToken cancellationToken)
    {
        var conversation = match.Conversations.FirstOrDefault(item => item.ConversationType == "LobbyChat");
        if (conversation is null)
        {
            conversation = new Conversation
            {
                MatchId = match.MatchId,
                ConversationType = "LobbyChat",
                ConversationName = match.Title,
                CreatedAt = DateTime.UtcNow
            };
            _db.Conversations.Add(conversation);
            match.Conversations.Add(conversation);
        }
        if (conversation.ConversationParticipants.All(item => item.UserId != userId))
        {
            conversation.ConversationParticipants.Add(new ConversationParticipant
            {
                UserId = userId,
                JoinedAt = DateTime.UtcNow
            });
        }
        await Task.CompletedTask;
    }

    private async Task RemoveConversationParticipantAsync(
        Match match,
        int userId,
        CancellationToken cancellationToken)
    {
        var conversation = match.Conversations.FirstOrDefault(item => item.ConversationType == "LobbyChat");
        var participant = conversation?.ConversationParticipants.FirstOrDefault(item => item.UserId == userId);
        if (participant is not null) _db.ConversationParticipants.Remove(participant);
        await Task.CompletedTask;
    }

    private static Booking? CurrentBooking(Match match) =>
        match.Bookings
            .Where(item => !InactiveBookingStatuses.Contains(item.Status))
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.BookingId)
            .FirstOrDefault();

    private static List<MatchParticipant> ApprovedParticipants(Match match) =>
        match.MatchParticipants.Where(IsApproved).ToList();

    private static bool IsApproved(MatchParticipant participant) =>
        participant.Status is "Approved" or "Accepted";

    private static List<int> PreferredVenueIds(Match match) =>
        (match.SharedVenues ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => int.TryParse(value, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .ToList();

    private static string NormalizeLegacyMatchStatus(string status) => status switch
    {
        "Waiting" => "Recruiting",
        "Full" => "ReadyToBook",
        "PaymentPending" => "BookingPending",
        "Confirmed" => "Booked",
        _ => status
    };

    private static double AmountPerPlayer(Match match, Booking booking) =>
        match.RequiredPlayerCount <= 0 ? 0 : Math.Ceiling(EffectiveMatchTotal(booking) / match.RequiredPlayerCount);

    private static double EffectiveMatchTotal(Booking booking)
    {
        if (booking.TotalAmount > 0) return booking.TotalAmount;
        var durationHours = Math.Max(0, (booking.EndTime - booking.StartTime).TotalHours);
        return Math.Round(EffectiveMatchHourlyPrice(booking) * durationHours, 0, MidpointRounding.AwayFromZero);
    }

    private static double EffectiveMatchHourlyPrice(Booking booking)
    {
        if (booking.HourlyPriceSnapshot > 0) return booking.HourlyPriceSnapshot;
        if (booking.Court.HourlyPrice > 0) return booking.Court.HourlyPrice;
        return MatchVenueBasePrice(booking.Court.Venue);
    }

    private static double MatchVenueBasePrice(Venue venue) =>
        double.TryParse(
            venue.BookingRules.FirstOrDefault(rule => rule.RuleType == "BasePrice")?.RuleContent,
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : 0;

    private static string? NormalizeMatchType(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant().Replace(" ", string.Empty);
        return normalized switch
        {
            "1vs1" or "1v1" => "1vs1",
            "2vs2" or "2v2" => "2vs2",
            _ => null
        };
    }

    private static bool TryParseMatchTime(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(
            value?.Trim(),
            ["HH:mm", "HH:mm:ss"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out time);

    private async Task<Player?> CurrentPlayerAsync(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        return userId is null
            ? null
            : await _db.Players.Include(item => item.User)
                .SingleOrDefaultAsync(item => item.UserId == userId.Value, cancellationToken);
    }

    private async Task<int?> CurrentPlayerIdAsync(CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        return userId is null
            ? null
            : await _db.Players.Where(item => item.UserId == userId.Value)
                .Select(item => (int?)item.PlayerId)
                .SingleOrDefaultAsync(cancellationToken);
    }


    private static BookingStatusHistory NewMatchBookingHistory(
        string? from,
        string to,
        string reason,
        int? actorUserId) => new()
    {
        FromStatus = from,
        ToStatus = to,
        Reason = reason,
        ActorUserId = actorUserId,
        ChangedAt = DateTime.UtcNow
    };

    private static PaymentStatusHistory NewMatchPaymentHistory(
        string? from,
        string to,
        string action,
        string? reason,
        int? actorUserId) => new()
    {
        FromStatus = from,
        ToStatus = to,
        Action = action,
        Reason = reason,
        ActorUserId = actorUserId,
        CreatedAt = DateTime.UtcNow
    };

    private static string BuildMatchVietQrUrl(OwnerBankAccount account, double amount, string content)
        => BuildMatchVietQrUrl(account.BankCode, account.AccountNumber, account.AccountHolderName, amount, content);

    private static string BuildMatchVietQrUrl(
        string bankCode,
        string accountNumber,
        string accountName,
        double amount,
        string content)
    {
        var query = $"amount={Math.Round(amount):0}&addInfo={Uri.EscapeDataString(content)}&accountName={Uri.EscapeDataString(accountName)}";
        return $"https://img.vietqr.io/image/{Uri.EscapeDataString(bankCode)}-{Uri.EscapeDataString(accountNumber)}-compact2.png?{query}";
    }

    private static double DistanceKm(double lat1, double lng1, double lat2, double lng2)
    {
        const double earthRadiusKm = 6371;
        var dLat = DegreesToRadians(lat2 - lat1);
        var dLng = DegreesToRadians(lng2 - lng1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2))
            * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 2 * earthRadiusKm * Math.Asin(Math.Sqrt(a));
    }

    private static double DegreesToRadians(double value) => value * Math.PI / 180;
    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static DateTime? AsUtc(DateTime? value) => value.HasValue ? AsUtc(value.Value) : null;
}

public class MatchSlotVoteRequest
{
    public int CourtId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
}

public class MatchSlotOptionResponse
{
    public int CourtId { get; set; }
    public int CourtNumber { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsCompatibleForAll { get; set; }
    public int CompatiblePlayerCount { get; set; }
    public int RequiredPlayerCount { get; set; }
    public int VoteCount { get; set; }
    public List<string> VoterNames { get; set; } = [];
    public bool IsVotedByMe { get; set; }
}
