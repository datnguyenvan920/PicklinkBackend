using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;

namespace PicklinkBackend.Services.Community;

public class CommunityDirectConversationService
{
    private readonly ApplicationDbContext _dbContext;

    public CommunityDirectConversationService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
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
            return DirectConversationServiceResult<DirectConversationResponse>.BadRequest("BÃ¡ÂºÂ¡n khÃƒÂ´ng thÃ¡Â»Æ’ tÃ¡Â»Â± trÃƒÂ² chuyÃ¡Â»â€¡n vÃ¡Â»â€ºi chÃƒÂ­nh mÃƒÂ¬nh.");
        }

        var targetUser = await _dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.UserId == targetUserId, cancellationToken);
        if (targetUser is null)
        {
            return DirectConversationServiceResult<DirectConversationResponse>.NotFound("KhÃƒÂ´ng tÃƒÂ¬m thÃ¡ÂºÂ¥y ngÃ†Â°Ã¡Â»Âi chÃ†Â¡i nÃƒÂ y.");
        }

        var existingConversationId = await _dbContext.Conversations
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

            _dbContext.Conversations.Add(conversation);
            await _dbContext.SaveChangesAsync(cancellationToken);
            conversationId = conversation.ConversationId;

            _dbContext.ConversationParticipants.Add(new ConversationParticipant
            {
                ConversationId = conversationId,
                UserId = userId.Value,
                JoinedAt = DateTime.UtcNow
            });

            _dbContext.ConversationParticipants.Add(new ConversationParticipant
            {
                ConversationId = conversationId,
                UserId = targetUserId,
                JoinedAt = DateTime.UtcNow
            });

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var targetPlayer = await _dbContext.Players
            .AsNoTracking()
            .Where(p => p.UserId == targetUserId)
            .Select(p => (double?)p.SkillLevel)
            .FirstOrDefaultAsync(cancellationToken);

        return DirectConversationServiceResult<DirectConversationResponse>.Success(new DirectConversationResponse(
            conversationId,
            targetUserId,
            targetUser.Username,
            targetUser.ProfileImageUrl,
            targetPlayer.HasValue ? targetPlayer.Value.ToString("0.0") : "3.5",
            DateTime.UtcNow,
            "BÃ¡ÂºÂ¯t Ã„â€˜Ã¡ÂºÂ§u cuÃ¡Â»â„¢c trÃƒÂ² chuyÃ¡Â»â€¡n mÃ¡Â»â€ºi"));
    }

    public async Task<DirectConversationServiceResult<IReadOnlyList<DirectConversationResponse>>> GetDirectConversationsAsync(
        int? userId,
        CancellationToken cancellationToken)
    {
        if (userId is null)
        {
            return DirectConversationServiceResult<IReadOnlyList<DirectConversationResponse>>.Unauthorized();
        }

        var directConversations = await _dbContext.Conversations
            .AsNoTracking()
            .Where(c => c.ConversationType == "Direct" && c.ConversationParticipants.Any(p => p.UserId == userId.Value))
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
            .ToListAsync(cancellationToken);

        var responseList = new List<DirectConversationResponse>();

        foreach (var conversation in directConversations)
        {
            var otherParticipant = await _dbContext.ConversationParticipants
                .AsNoTracking()
                .Include(p => p.User)
                .Where(p => p.ConversationId == conversation.ConversationId && p.UserId != userId.Value)
                .FirstOrDefaultAsync(cancellationToken);

            if (otherParticipant is null)
            {
                continue;
            }

            var otherUser = otherParticipant.User;
            var otherPlayerLevel = await _dbContext.Players
                .AsNoTracking()
                .Where(p => p.UserId == otherUser.UserId)
                .Select(p => (double?)p.SkillLevel)
                .FirstOrDefaultAsync(cancellationToken);

            var lastMessage = await _dbContext.Messages
                .AsNoTracking()
                .Where(m => m.ConversationId == conversation.ConversationId && !m.IsDeleted)
                .OrderByDescending(m => m.MessageId)
                .FirstOrDefaultAsync(cancellationToken);

            responseList.Add(new DirectConversationResponse(
                conversation.ConversationId,
                otherUser.UserId,
                otherUser.Username,
                otherUser.ProfileImageUrl,
                otherPlayerLevel.HasValue ? otherPlayerLevel.Value.ToString("0.0") : "3.5",
                conversation.LastMessageAt ?? conversation.CreatedAt,
                lastMessage?.Content ?? "ChÃ†Â°a cÃƒÂ³ tin nhÃ¡ÂºÂ¯n"));
        }

        return DirectConversationServiceResult<IReadOnlyList<DirectConversationResponse>>.Success(responseList);
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

        var isParticipant = await _dbContext.ConversationParticipants
            .AnyAsync(p => p.ConversationId == conversationId && p.UserId == userId.Value, cancellationToken);

        if (!isParticipant)
        {
            return DirectConversationServiceResult<IReadOnlyList<CommunityMessageResponse>>.Forbidden();
        }

        var query = _dbContext.Messages
            .AsNoTracking()
            .Where(message => message.ConversationId == conversationId && !message.IsDeleted);

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

        var isParticipant = await _dbContext.ConversationParticipants
            .AnyAsync(p => p.ConversationId == conversationId && p.UserId == userId.Value, cancellationToken);

        if (!isParticipant)
        {
            return DirectConversationServiceResult<CommunityMessageResponse>.Forbidden();
        }

        if (string.IsNullOrWhiteSpace(request.Content) && string.IsNullOrWhiteSpace(request.MediaUrl))
        {
            return DirectConversationServiceResult<CommunityMessageResponse>.BadRequest("NÃ¡Â»â„¢i dung tin nhÃ¡ÂºÂ¯n khÃƒÂ´ng thÃ¡Â»Æ’ trÃ¡Â»â€˜ng.");
        }

        var now = DateTime.UtcNow;
        var message = new Message
        {
            ConversationId = conversationId,
            SenderId = userId.Value,
            Content = request.Content?.Trim(),
            MessageType = string.IsNullOrWhiteSpace(request.MediaUrl) ? "Text" : "Image",
            MediaUrl = request.MediaUrl?.Trim(),
            ReplyToMessageId = request.ReplyToMessageId,
            SentAt = now
        };

        _dbContext.Messages.Add(message);

        var conversation = await _dbContext.Conversations
            .SingleOrDefaultAsync(c => c.ConversationId == conversationId, cancellationToken);
        if (conversation is not null)
        {
            conversation.LastMessageAt = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var sender = await _dbContext.Users
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
}

public sealed record DirectConversationServiceResult<T>(
    DirectConversationServiceResultStatus Status,
    T? Value = default,
    string? ErrorMessage = null)
{
    public static DirectConversationServiceResult<T> Success(T value) =>
        new(DirectConversationServiceResultStatus.Success, value);

    public static DirectConversationServiceResult<T> BadRequest(string errorMessage) =>
        new(DirectConversationServiceResultStatus.BadRequest, ErrorMessage: errorMessage);

    public static DirectConversationServiceResult<T> Unauthorized() =>
        new(DirectConversationServiceResultStatus.Unauthorized);

    public static DirectConversationServiceResult<T> Forbidden() =>
        new(DirectConversationServiceResultStatus.Forbidden);

    public static DirectConversationServiceResult<T> NotFound(string errorMessage) =>
        new(DirectConversationServiceResultStatus.NotFound, ErrorMessage: errorMessage);
}

public enum DirectConversationServiceResultStatus
{
    Success,
    BadRequest,
    Unauthorized,
    Forbidden,
    NotFound
}
