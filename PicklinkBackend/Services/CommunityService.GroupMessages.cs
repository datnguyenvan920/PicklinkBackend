using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;

namespace PicklinkBackend.Services;

public partial class CommunityService
{
    [HttpGet("groups/{groupId:int}/messages")]
    public async Task<ActionResult<IReadOnlyList<CommunityMessageResponse>>> Messages(
        int groupId,
        [FromQuery] int? beforeMessageId,
        [FromQuery] int limit = 8,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (!await CanInteractWithGroupAsync(groupId, userId.Value, cancellationToken))
        {
            return Forbid();
        }

        var conversation = await EnsureGroupConversationAsync(groupId, cancellationToken);
        await EnsureConversationParticipantAsync(conversation.ConversationId, userId.Value, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var query = _dbContext.Messages
            .AsNoTracking()
            .Where(message => message.ConversationId == conversation.ConversationId && !message.IsDeleted);

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

    [HttpGet("groups/{groupId:int}/messages/pinned")]
    public async Task<ActionResult<IReadOnlyList<CommunityMessageResponse>>> PinnedMessages(
        int groupId,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (!await CanInteractWithGroupAsync(groupId, userId.Value, cancellationToken))
        {
            return Forbid();
        }

        var conversation = await EnsureGroupConversationAsync(groupId, cancellationToken);
        await EnsureConversationParticipantAsync(conversation.ConversationId, userId.Value, cancellationToken);

        var messages = await _dbContext.Messages
            .AsNoTracking()
            .Where(message => message.ConversationId == conversation.ConversationId && !message.IsDeleted && message.IsPinned)
            .OrderBy(message => message.SentAt)
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

        return Ok(messages);
    }

    [HttpPost("groups/{groupId:int}/messages")]
    public async Task<ActionResult<CommunityMessageResponse>> SendMessage(
        int groupId,
        SendCommunityMessageRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (!await CanInteractWithGroupAsync(groupId, userId.Value, cancellationToken))
        {
            return Forbid();
        }

        var content = NormalizeOptional(request.Content);
        var mediaUrl = NormalizeOptional(request.MediaUrl);
        if (content is null && mediaUrl is null)
        {
            return BadRequest(new { message = "Message content or media is required." });
        }

        var conversation = await EnsureGroupConversationAsync(groupId, cancellationToken);
        await EnsureConversationParticipantAsync(conversation.ConversationId, userId.Value, cancellationToken);

        var now = DateTime.UtcNow;
        var message = new Message
        {
            ConversationId = conversation.ConversationId,
            SenderId = userId.Value,
            Content = content,
            MessageType = mediaUrl is null ? "Text" : "Media",
            MediaUrl = mediaUrl,
            ReplyToMessageId = request.ReplyToMessageId,
            SentAt = now,
            IsDeleted = false,
            IsPinned = false
        };

        conversation.LastMessageAt = now;
        _dbContext.Messages.Add(message);
        await NotifyGroupMembersAsync(groupId, userId.Value, "New group message.", cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _notifications.PublishPending();

        var response = await _dbContext.Messages
            .AsNoTracking()
            .Where(savedMessage => savedMessage.MessageId == message.MessageId)
            .Select(savedMessage => new CommunityMessageResponse(
                savedMessage.MessageId,
                savedMessage.ConversationId,
                savedMessage.SenderId,
                savedMessage.Sender.Username,
                savedMessage.Sender.ProfileImageUrl,
                savedMessage.Content,
                savedMessage.MessageType,
                savedMessage.MediaUrl,
                savedMessage.ReplyToMessageId,
                savedMessage.SentAt,
                true,
                savedMessage.IsPinned))
            .SingleAsync(cancellationToken);

        return Ok(response);
    }

    [HttpDelete("groups/{groupId:int}/messages/{messageId:int}")]
    public async Task<ActionResult> DeleteMessage(
        int groupId,
        int messageId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var message = await _dbContext.Messages
            .Include(m => m.Conversation)
            .SingleOrDefaultAsync(m => m.MessageId == messageId, cancellationToken);

        if (message is null || message.Conversation.GroupId != groupId)
        {
            return NotFound();
        }

        var member = await GetMembershipAsync(groupId, userId.Value, cancellationToken);
        var isManager = IsGroupManager(member);

        if (message.SenderId != userId.Value && !isManager)
        {
            return Forbid();
        }

        message.IsDeleted = true;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpPut("groups/{groupId:int}/messages/{messageId:int}/pin")]
    public async Task<ActionResult<CommunityMessageResponse>> PinMessage(
        int groupId,
        int messageId,
        [FromQuery] bool pin,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var message = await _dbContext.Messages
            .Include(m => m.Conversation)
            .SingleOrDefaultAsync(m => m.MessageId == messageId, cancellationToken);

        if (message is null || message.Conversation.GroupId != groupId)
        {
            return NotFound();
        }

        var member = await GetMembershipAsync(groupId, userId.Value, cancellationToken);
        if (!IsGroupManager(member))
        {
            return Forbid();
        }

        message.IsPinned = pin;
        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = await _dbContext.Messages
            .AsNoTracking()
            .Where(m => m.MessageId == messageId)
            .Select(m => new CommunityMessageResponse(
                m.MessageId,
                m.ConversationId,
                m.SenderId,
                m.Sender.Username,
                m.Sender.ProfileImageUrl,
                m.Content,
                m.MessageType,
                m.MediaUrl,
                m.ReplyToMessageId,
                m.SentAt,
                m.SenderId == userId.Value,
                m.IsPinned))
            .SingleAsync(cancellationToken);

        return Ok(response);
    }
}
