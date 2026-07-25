using Microsoft.EntityFrameworkCore;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Repositories;
using PicklinkBackend.Services.Bookings;
using PicklinkBackend.Services.Community;
using PicklinkBackend.Services.Shared;

namespace PicklinkBackend.Services.Community.Implementations;

public class CommunityDirectConversationService
{
    private readonly ICommunityRepository _communityRepository;

    public CommunityDirectConversationService(ICommunityRepository communityRepository)
    {
        _communityRepository = communityRepository;
    }

    public async Task<DirectConversationServiceResult<DirectConversationResponse>> StartDirectConversationAsync(
        int? userId,
        int targetUserId,
        CancellationToken cancellationToken)
    {
        if (userId is null)
        {
            return DirectConversationServiceResult<DirectConversationResponse>.Unauthorized();
        }

        if (userId.Value == targetUserId)
        {
            return DirectConversationServiceResult<DirectConversationResponse>.BadRequest("Bạn không thể tự trò chuyện với chính mình.");
        }

        var targetUser = await _communityRepository.Users
            .AsNoTracking()
            .Where(u => u.UserId == targetUserId)
            .Select(u => new
            {
                u.Username,
                u.ProfileImageUrl,
                SkillLevel = u.Players
                    .Select(player => (double?)player.SkillLevel)
                    .FirstOrDefault()
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (targetUser is null)
        {
            return DirectConversationServiceResult<DirectConversationResponse>.NotFound("Không tìm thấy người chơi này.");
        }

        var firstUserId = Math.Min(userId.Value, targetUserId);
        var secondUserId = Math.Max(userId.Value, targetUserId);
        await using var transaction = await _communityRepository.BeginTransactionAsync(cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(
                transaction,
                $"direct-conversation:{firstUserId}:{secondUserId}",
                cancellationToken))
        {
            throw new TimeoutException("Timed out waiting to start the direct conversation.");
        }

        var existingConversationId = await _communityRepository.Conversations
            .Where(c => c.ConversationType == "Direct")
            .Where(c => c.ConversationParticipants.Any(p => p.UserId == userId.Value) &&
                        c.ConversationParticipants.Any(p => p.UserId == targetUserId))
            .Select(c => (int?)c.ConversationId)
            .FirstOrDefaultAsync(cancellationToken);

        int conversationId;
        if (existingConversationId.HasValue)
        {
            conversationId = existingConversationId.Value;
        }
        else
        {
            var now = DateTime.UtcNow;
            var conversation = new Conversation
            {
                ConversationType = "Direct",
                ConversationName = $"Direct {userId.Value} - {targetUserId}",
                CreatedAt = now,
                LastMessageAt = now
            };

            conversation.ConversationParticipants.Add(new ConversationParticipant
            {
                UserId = userId.Value,
                JoinedAt = now
            });
            conversation.ConversationParticipants.Add(new ConversationParticipant
            {
                UserId = targetUserId,
                JoinedAt = now
            });

            await _communityRepository.AddConversationAsync(conversation, cancellationToken);
            await _communityRepository.SaveChangesAsync(cancellationToken);
            conversationId = conversation.ConversationId;
        }

        await transaction.CommitAsync(cancellationToken);

        return DirectConversationServiceResult<DirectConversationResponse>.Success(new DirectConversationResponse(
            conversationId,
            targetUserId,
            targetUser.Username,
            targetUser.ProfileImageUrl,
            targetUser.SkillLevel.HasValue ? targetUser.SkillLevel.Value.ToString("0.0") : "3.5",
            DateTime.UtcNow,
            "Bắt đầu cuộc trò chuyện mới"));
    }

    public async Task<DirectConversationServiceResult<IReadOnlyList<DirectConversationResponse>>> GetDirectConversationsAsync(
        int? userId,
        CancellationToken cancellationToken)
    {
        if (userId is null)
        {
            return DirectConversationServiceResult<IReadOnlyList<DirectConversationResponse>>.Unauthorized();
        }

        var directConversations = await _communityRepository.Conversations
            .AsNoTracking()
            .Where(c => (c.ConversationType == "Direct" || c.ConversationType == "QueueLobbyChat" || c.ConversationType == "LobbyChat") && c.ConversationParticipants.Any(p => p.UserId == userId.Value))
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
            .Select(c => new
            {
                c.ConversationId,
                c.ConversationType,
                c.MatchId,
                c.ConversationName,
                LastMessageAt = c.LastMessageAt ?? c.CreatedAt,
                LastMessage = c.Messages
                    .Where(message => !message.IsDeleted)
                    .OrderByDescending(message => message.MessageId)
                    .Select(message => message.Content)
                    .FirstOrDefault(),
                OtherParticipant = c.ConversationParticipants
                    .Where(participant => participant.UserId != userId.Value)
                    .Select(participant => new
                    {
                        participant.UserId,
                        participant.User.Username,
                        participant.User.ProfileImageUrl,
                        SkillLevel = participant.User.Players
                            .Select(player => (double?)player.SkillLevel)
                            .FirstOrDefault()
                    })
                    .FirstOrDefault(),
                UnreadMessageCount = c.ConversationParticipants
                    .Where(participant => participant.UserId == userId.Value)
                    .Select(participant => c.Messages.Count(message =>
                        !message.IsDeleted &&
                        message.SenderId != userId.Value &&
                        message.SentAt >= participant.JoinedAt &&
                        (!participant.LastReadAt.HasValue || message.SentAt > participant.LastReadAt.Value)))
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var responseList = new List<DirectConversationResponse>();

        foreach (var conversation in directConversations)
        {
            var chatAccess = conversation.ConversationType == "LobbyChat"
                ? await ResolveChatAccessAsync(conversation.ConversationId, userId.Value, cancellationToken)
                : new MatchLobbyChatAccess(true, false, null, null);
            if (!chatAccess.IsAllowed) continue;

            if (conversation.ConversationType == "Direct")
            {
                var otherParticipant = conversation.OtherParticipant;
                if (otherParticipant is null)
                {
                    continue;
                }

                responseList.Add(new DirectConversationResponse(
                    conversation.ConversationId,
                    otherParticipant.UserId,
                    otherParticipant.Username,
                    otherParticipant.ProfileImageUrl,
                    otherParticipant.SkillLevel.HasValue ? otherParticipant.SkillLevel.Value.ToString("0.0") : "3.5",
                    conversation.LastMessageAt,
                    conversation.LastMessage ?? "Chưa có tin nhắn",
                    conversation.UnreadMessageCount));
            }
            else
            {
                var canSeeLatestMessage = !chatAccess.VisibleFromUtc.HasValue
                    || conversation.LastMessageAt >= chatAccess.VisibleFromUtc.Value;
                responseList.Add(new DirectConversationResponse(
                    conversation.ConversationId,
                    0,
                    conversation.ConversationName ?? (conversation.ConversationType == "QueueLobbyChat" ? "Hàng chờ ghép trận" : "Phòng ghép trận"),
                    null,
                    "",
                    canSeeLatestMessage ? conversation.LastMessageAt : chatAccess.VisibleFromUtc!.Value,
                    canSeeLatestMessage ? conversation.LastMessage ?? "Chưa có tin nhắn" : "Chưa có tin nhắn",
                    conversation.UnreadMessageCount,
                    conversation.ConversationType,
                    conversation.MatchId,
                    chatAccess.IsTemporaryReplacement ? "Replacement" : "Member",
                    chatAccess.ExpiresAtUtc));
            }
        }

        return DirectConversationServiceResult<IReadOnlyList<DirectConversationResponse>>.Success(responseList);
    }

    public async Task<DirectConversationServiceResult<UnreadMessageSenderCountResponse>> CountUnreadSendersAsync(
        int? userId,
        CancellationToken cancellationToken)
    {
        if (userId is null)
        {
            return DirectConversationServiceResult<UnreadMessageSenderCountResponse>.Unauthorized();
        }

        var activeReplacementCutoff = VietnamTime.Now.AddHours(-2);
        var count = await _communityRepository.ConversationParticipants
            .AsNoTracking()
            .Where(participant => participant.UserId == userId.Value
                && (participant.Conversation.ConversationType != "LobbyChat"
                    || !participant.Conversation.MatchId.HasValue
                    || participant.Conversation.Match!.MatchParticipants.Any(member =>
                        member.Player.UserId == userId.Value
                        && (member.Status == "Approved" || member.Status == "Accepted"))
                    || participant.Conversation.Match!.SlotAbsences.Any(absence =>
                        absence.BookingCheckInGroup.EndTime > activeReplacementCutoff
                        && (absence.BookingCheckInGroup.Booking.Status == "Holding"
                            || absence.BookingCheckInGroup.Booking.Status == "Confirmed")
                        && absence.ReplacementRequests.Any(request => request.Player.UserId == userId.Value
                            && request.Status == "Approved"))))
            .SelectMany(participant => participant.Conversation.Messages
                .Where(message =>
                    !message.IsDeleted &&
                    message.SenderId != userId.Value &&
                    message.SentAt >= participant.JoinedAt &&
                    (!participant.LastReadAt.HasValue || message.SentAt > participant.LastReadAt.Value)))
            .Select(message => message.SenderId)
            .Distinct()
            .CountAsync(cancellationToken);

        return DirectConversationServiceResult<UnreadMessageSenderCountResponse>.Success(
            new UnreadMessageSenderCountResponse(count));
    }

    public async Task<DirectConversationServiceResult<IReadOnlyList<CommunityMessageResponse>>> GetDirectMessagesAsync(
        int? userId,
        int conversationId,
        int? beforeMessageId,
        int limit,
        CancellationToken cancellationToken)
    {
        if (userId is null)
        {
            return DirectConversationServiceResult<IReadOnlyList<CommunityMessageResponse>>.Unauthorized();
        }

        var participant = await _communityRepository.ConversationParticipants
            .SingleOrDefaultAsync(
                p => p.ConversationId == conversationId && p.UserId == userId.Value,
                cancellationToken);

        if (participant is null)
        {
            return DirectConversationServiceResult<IReadOnlyList<CommunityMessageResponse>>.Forbidden();
        }

        var chatAccess = await ResolveChatAccessAsync(conversationId, userId.Value, cancellationToken);
        if (!chatAccess.IsAllowed)
            return DirectConversationServiceResult<IReadOnlyList<CommunityMessageResponse>>.Forbidden();

        var query = _communityRepository.Messages
            .AsNoTracking()
            .Where(message => message.ConversationId == conversationId && !message.IsDeleted);
        if (chatAccess.VisibleFromUtc.HasValue)
            query = query.Where(message => message.SentAt >= chatAccess.VisibleFromUtc.Value);

        if (beforeMessageId.HasValue)
        {
            query = query.Where(message => message.MessageId < beforeMessageId.Value);
        }

        var messages = await query
            .OrderByDescending(message => message.MessageId)
            .Take(limit)
            .Select(message => new CommunityMessageResponse(
                message.MessageId,
                message.ConversationId,
                message.SenderId,
                message.Sender.Username,
                message.Sender.ProfileImageUrl,
                message.Content,
                message.MessageType,
                message.MediaUrl,
                message.ReplyToMessageId,
                message.SentAt,
                message.SenderId == userId.Value,
                message.IsPinned))
            .ToListAsync(cancellationToken);

        messages.Reverse();

        if (!beforeMessageId.HasValue)
        {
            participant.LastReadAt = DateTime.UtcNow;
            await _communityRepository.SaveChangesAsync(cancellationToken);
        }

        return DirectConversationServiceResult<IReadOnlyList<CommunityMessageResponse>>.Success(messages);
    }

    public async Task<DirectConversationServiceResult<CommunityMessageResponse>> SendDirectMessageAsync(
        int? userId,
        int conversationId,
        SendCommunityMessageRequest request,
        CancellationToken cancellationToken)
    {
        if (userId is null)
        {
            return DirectConversationServiceResult<CommunityMessageResponse>.Unauthorized();
        }

        var chatAccess = await ResolveChatAccessAsync(conversationId, userId.Value, cancellationToken);
        if (!chatAccess.IsAllowed)
        {
            return DirectConversationServiceResult<CommunityMessageResponse>.Forbidden();
        }

        var validation = MessageInputPolicy.Validate(request.Content, request.MediaUrl);
        if (!validation.IsValid)
            return DirectConversationServiceResult<CommunityMessageResponse>.BadRequest(validation.ErrorMessage!);

        if (request.ReplyToMessageId.HasValue && !await _communityRepository.Messages
                .AsNoTracking()
                .AnyAsync(message =>
                    message.MessageId == request.ReplyToMessageId.Value
                    && message.ConversationId == conversationId
                    && !message.IsDeleted,
                    cancellationToken))
            return DirectConversationServiceResult<CommunityMessageResponse>.BadRequest(
                "Tin nhắn được trả lời không thuộc cuộc trò chuyện này.");

        var now = DateTime.UtcNow;
        var message = new Message
        {
            ConversationId = conversationId,
            SenderId = userId.Value,
            Content = validation.Content,
            MessageType = validation.MediaUrl is null ? "Text" : "Image",
            MediaUrl = validation.MediaUrl,
            ReplyToMessageId = request.ReplyToMessageId,
            SentAt = now
        };

        await _communityRepository.AddMessageAsync(message, cancellationToken);

        var conversation = await _communityRepository.GetConversationByIdAsync(conversationId, cancellationToken);
        if (conversation is not null)
        {
            conversation.LastMessageAt = now;
        }

        await _communityRepository.SaveChangesAsync(cancellationToken);

        var sender = await _communityRepository.Users
            .AsNoTracking()
            .SingleAsync(u => u.UserId == userId.Value, cancellationToken);

        return DirectConversationServiceResult<CommunityMessageResponse>.Success(new CommunityMessageResponse(
            message.MessageId,
            message.ConversationId,
            message.SenderId,
            sender.Username,
            sender.ProfileImageUrl,
            message.Content,
            message.MessageType,
            message.MediaUrl,
            message.ReplyToMessageId,
            message.SentAt,
            true,
            false));
    }

    private async Task<MatchLobbyChatAccess> ResolveChatAccessAsync(int conversationId, int userId, CancellationToken cancellationToken)
    {
        var participant = await _communityRepository.ConversationParticipants
            .AsNoTracking()
            .Where(item => item.ConversationId == conversationId && item.UserId == userId)
            .Select(item => new { item.Conversation.MatchId, item.Conversation.ConversationType })
            .SingleOrDefaultAsync(cancellationToken);

        if (participant is null) return MatchLobbyChatAccess.Denied;
        if (participant.ConversationType != "LobbyChat" || !participant.MatchId.HasValue)
            return new MatchLobbyChatAccess(true, false, null, null);

        var isApprovedMember = await _communityRepository.MatchParticipants
            .AsNoTracking()
            .AnyAsync(item => item.MatchId == participant.MatchId.Value
                && item.Player.UserId == userId
                && (item.Status == "Approved" || item.Status == "Accepted"), cancellationToken);
        if (isApprovedMember) return new MatchLobbyChatAccess(true, false, null, null);

        var approvedSlots = await _communityRepository.MatchSlotReplacementRequests
            .AsNoTracking()
            .Where(item => item.MatchSlotAbsence.MatchId == participant.MatchId.Value
                && (item.MatchSlotAbsence.BookingCheckInGroup.Booking.Status == "Holding"
                    || item.MatchSlotAbsence.BookingCheckInGroup.Booking.Status == "Confirmed")
                && item.Player.UserId == userId
                && item.Status == "Approved")
            .Select(item => new { item.RequestedAt, item.RespondedAt, item.MatchSlotAbsence.BookingCheckInGroup.EndTime })
            .ToListAsync(cancellationToken);

        var localNow = VietnamTime.Now;
        var activeSlots = approvedSlots.Where(slot => slot.EndTime.AddHours(2) > localNow).ToList();
        if (activeSlots.Count == 0) return MatchLobbyChatAccess.Denied;

        var visibleFrom = activeSlots.Min(slot => slot.RespondedAt ?? slot.RequestedAt);
        var activeExpiry = activeSlots.Max(slot => slot.EndTime.AddHours(2));

        return new MatchLobbyChatAccess(true, true, DateTime.SpecifyKind(visibleFrom, DateTimeKind.Utc), VietnamTime.ToUtc(activeExpiry));
    }
}
