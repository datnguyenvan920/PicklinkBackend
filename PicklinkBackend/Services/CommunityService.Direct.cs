using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;

namespace PicklinkBackend.Services;

public partial class CommunityService
{
    [HttpGet("players/outstanding")]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<OutstandingPlayerResponse>>> GetOutstandingPlayers(
        CancellationToken cancellationToken)
    {
        var players = await _dbContext.Players
            .AsNoTracking()
            .Include(p => p.User)
            .OrderByDescending(p => p.Prestige)
            .ThenByDescending(p => p.SkillLevel)
            .Take(5)
            .Select(p => new OutstandingPlayerResponse(
                p.User.UserId,
                p.User.Username,
                p.SkillLevel.ToString("0.0"),
                p.User.ProfileImageUrl
            ))
            .ToListAsync(cancellationToken);

        return Ok(players);
    }

    [HttpPost("conversations/direct/start")]
    public async Task<ActionResult<DirectConversationResponse>> StartDirectConversation(
        [FromQuery] int targetUserId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (userId.Value == targetUserId)
        {
            return BadRequest(new { message = "Bạn không thể tự trò chuyện với chính mình." });
        }

        // Verify the target user exists
        var targetUser = await _dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.UserId == targetUserId, cancellationToken);
        if (targetUser is null)
        {
            return NotFound(new { message = "Không tìm thấy người chơi này." });
        }

        // Find existing direct conversation between these two users
        var existingConvId = await _dbContext.Conversations
            .Where(c => c.ConversationType == "Direct")
            .Where(c => c.ConversationParticipants.Any(p => p.UserId == userId.Value) &&
                        c.ConversationParticipants.Any(p => p.UserId == targetUserId))
            .Select(c => (int?)c.ConversationId)
            .FirstOrDefaultAsync(cancellationToken);

        int conversationId;
        if (existingConvId.HasValue)
        {
            conversationId = existingConvId.Value;
        }
        else
        {
            // Create a new direct conversation
            var conversation = new Conversation
            {
                ConversationType = "Direct",
                ConversationName = $"Direct {userId.Value} - {targetUserId}",
                CreatedAt = DateTime.UtcNow,
                LastMessageAt = DateTime.UtcNow
            };

            _dbContext.Conversations.Add(conversation);
            await _dbContext.SaveChangesAsync(cancellationToken);
            conversationId = conversation.ConversationId;

            // Add both participants
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

        // Fetch user information to return response
        var targetPlayer = await _dbContext.Players
            .AsNoTracking()
            .Where(p => p.UserId == targetUserId)
            .Select(p => (double?)p.SkillLevel)
            .FirstOrDefaultAsync(cancellationToken);

        return Ok(new DirectConversationResponse(
            conversationId,
            targetUserId,
            targetUser.Username,
            targetUser.ProfileImageUrl,
            targetPlayer.HasValue ? targetPlayer.Value.ToString("0.0") : "3.5",
            DateTime.UtcNow,
            "Bắt đầu cuộc trò chuyện mới"
        ));
    }

    [HttpGet("conversations/direct")]
    public async Task<ActionResult<IReadOnlyList<DirectConversationResponse>>> GetDirectConversations(
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        // Find all direct conversations for this user
        var directConversations = await _dbContext.Conversations
            .AsNoTracking()
            .Where(c => c.ConversationType == "Direct" && c.ConversationParticipants.Any(p => p.UserId == userId.Value))
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
            .ToListAsync(cancellationToken);

        var responseList = new List<DirectConversationResponse>();

        foreach (var conv in directConversations)
        {
            // Find the other participant
            var otherParticipant = await _dbContext.ConversationParticipants
                .AsNoTracking()
                .Include(p => p.User)
                .Where(p => p.ConversationId == conv.ConversationId && p.UserId != userId.Value)
                .FirstOrDefaultAsync(cancellationToken);

            if (otherParticipant == null) continue;

            var otherUser = otherParticipant.User;

            // Get other player level info
            var otherPlayerLevel = await _dbContext.Players
                .AsNoTracking()
                .Where(p => p.UserId == otherUser.UserId)
                .Select(p => (double?)p.SkillLevel)
                .FirstOrDefaultAsync(cancellationToken);

            // Get last message in this conversation
            var lastMsg = await _dbContext.Messages
                .AsNoTracking()
                .Where(m => m.ConversationId == conv.ConversationId && !m.IsDeleted)
                .OrderByDescending(m => m.MessageId)
                .FirstOrDefaultAsync(cancellationToken);

            responseList.Add(new DirectConversationResponse(
                conv.ConversationId,
                otherUser.UserId,
                otherUser.Username,
                otherUser.ProfileImageUrl,
                otherPlayerLevel.HasValue ? otherPlayerLevel.Value.ToString("0.0") : "3.5",
                conv.LastMessageAt ?? conv.CreatedAt,
                lastMsg?.Content ?? "Chưa có tin nhắn"
            ));
        }

        return Ok(responseList);
    }

    [HttpGet("conversations/direct/{conversationId:int}/messages")]
    public async Task<ActionResult<IReadOnlyList<CommunityMessageResponse>>> GetDirectMessages(
        int conversationId,
        [FromQuery] int? beforeMessageId,
        [FromQuery] int limit = 8,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        // Verify the user is a participant of this conversation
        var isParticipant = await _dbContext.ConversationParticipants
            .AnyAsync(p => p.ConversationId == conversationId && p.UserId == userId.Value, cancellationToken);

        if (!isParticipant)
        {
            return Forbid();
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
            .Take(8)
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

        return Ok(messages);
    }

    [HttpPost("conversations/direct/{conversationId:int}/messages")]
    public async Task<ActionResult<CommunityMessageResponse>> SendDirectMessage(
        int conversationId,
        [FromBody] SendCommunityMessageRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        // Verify participant
        var isParticipant = await _dbContext.ConversationParticipants
            .AnyAsync(p => p.ConversationId == conversationId && p.UserId == userId.Value, cancellationToken);

        if (!isParticipant)
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.Content) && string.IsNullOrWhiteSpace(request.MediaUrl))
        {
            return BadRequest(new { message = "Nội dung tin nhắn không thể trống." });
        }

        var message = new Message
        {
            ConversationId = conversationId,
            SenderId = userId.Value,
            Content = request.Content?.Trim(),
            MessageType = string.IsNullOrWhiteSpace(request.MediaUrl) ? "Text" : "Image",
            MediaUrl = request.MediaUrl?.Trim(),
            ReplyToMessageId = request.ReplyToMessageId,
            SentAt = DateTime.UtcNow
        };

        _dbContext.Messages.Add(message);

        // Update conversation last message timestamp
        var conversation = await _dbContext.Conversations
            .SingleOrDefaultAsync(c => c.ConversationId == conversationId, cancellationToken);
        if (conversation != null)
        {
            conversation.LastMessageAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Load sender info
        var sender = await _dbContext.Users
            .AsNoTracking()
            .SingleAsync(u => u.UserId == userId.Value, cancellationToken);

        return Ok(new CommunityMessageResponse(
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
            false
        ));
    }

    [HttpGet("friends")]
    public async Task<ActionResult<IReadOnlyList<FriendResponse>>> GetFriends(
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var friends = await _dbContext.Friendships
            .AsNoTracking()
            .Where(f => (f.RequesterId == userId.Value || f.ReceiverId == userId.Value) && f.Status == "Accepted")
            .Select(f => f.RequesterId == userId.Value ? f.Receiver : f.Requester)
            .Select(u => new FriendResponse(
                u.UserId,
                u.Username,
                u.ProfileImageUrl
            ))
            .ToListAsync(cancellationToken);

        return Ok(friends);
    }
}
