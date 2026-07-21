using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;

namespace PicklinkBackend.Services.Shared;

public sealed record MatchLobbyChatAccess(
    bool IsAllowed,
    bool IsTemporaryReplacement,
    DateTime? VisibleFromUtc,
    DateTime? ExpiresAtUtc)
{
    public static MatchLobbyChatAccess Denied { get; } = new(false, false, null, null);
}

public static class MatchLobbyChatAccessPolicy
{
    public static async Task<MatchLobbyChatAccess> ResolveAsync(
        ApplicationDbContext dbContext,
        int conversationId,
        int userId,
        CancellationToken cancellationToken = default)
    {
        var participant = await dbContext.ConversationParticipants
            .AsNoTracking()
            .Where(item => item.ConversationId == conversationId && item.UserId == userId)
            .Select(item => new
            {
                item.Conversation.MatchId,
                item.Conversation.ConversationType
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (participant is null) return MatchLobbyChatAccess.Denied;
        if (participant.ConversationType != "LobbyChat" || !participant.MatchId.HasValue)
            return new MatchLobbyChatAccess(true, false, null, null);

        var isApprovedMember = await dbContext.MatchParticipants
            .AsNoTracking()
            .AnyAsync(item => item.MatchId == participant.MatchId.Value
                && item.Player.UserId == userId
                && (item.Status == "Approved" || item.Status == "Accepted"), cancellationToken);
        if (isApprovedMember) return new MatchLobbyChatAccess(true, false, null, null);

        var approvedSlots = await dbContext.MatchSlotReplacementRequests
            .AsNoTracking()
            .Where(item => item.MatchSlotAbsence.MatchId == participant.MatchId.Value
                && (item.MatchSlotAbsence.BookingCheckInGroup.Booking.Status == "Holding"
                    || item.MatchSlotAbsence.BookingCheckInGroup.Booking.Status == "Confirmed")
                && item.Player.UserId == userId
                && item.Status == "Approved")
            .Select(item => new
            {
                item.RequestedAt,
                item.RespondedAt,
                item.MatchSlotAbsence.BookingCheckInGroup.EndTime
            })
            .ToListAsync(cancellationToken);

        var localNow = VietnamTime.Now;
        var activeSlots = approvedSlots
            .Where(slot => slot.EndTime.AddHours(2) > localNow)
            .ToList();
        if (activeSlots.Count == 0) return MatchLobbyChatAccess.Denied;

        var visibleFrom = activeSlots.Min(slot => slot.RespondedAt ?? slot.RequestedAt);
        var activeExpiry = activeSlots.Max(slot => slot.EndTime.AddHours(2));

        return new MatchLobbyChatAccess(
            true,
            true,
            DateTime.SpecifyKind(visibleFrom, DateTimeKind.Utc),
            VietnamTime.ToUtc(activeExpiry));
    }
}
