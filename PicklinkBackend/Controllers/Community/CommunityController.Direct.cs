using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PicklinkBackend.Startup;
using PicklinkBackend.DTOs;

namespace PicklinkBackend.Controllers;

public partial class CommunityController
{
    [HttpGet("players/outstanding")]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<OutstandingPlayerResponse>>> GetOutstandingPlayers(
        CancellationToken cancellationToken)
    {
        return Ok(await _discoveryService.GetOutstandingPlayersAsync(cancellationToken));
    }

    [HttpGet("players/search")]
    public async Task<ActionResult<IReadOnlyList<PlayerSearchResultResponse>>> SearchPlayers(
        [FromQuery] string? query,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        SetCommunityUser();
        return ToActionResult(await _community.SearchPlayers(query, limit, cancellationToken));
    }

    [HttpPost("conversations/direct/start")]
    public async Task<ActionResult<DirectConversationResponse>> StartDirectConversation(
        [FromQuery] int targetUserId,
        CancellationToken cancellationToken)
    {
        var result = await _directConversations.StartDirectConversationAsync(
            GetCurrentUserIdFromClaims(),
            targetUserId,
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpGet("conversations/direct")]
    public async Task<ActionResult<IReadOnlyList<DirectConversationResponse>>> GetDirectConversations(
        CancellationToken cancellationToken)
    {
        var result = await _directConversations.GetDirectConversationsAsync(
            GetCurrentUserIdFromClaims(),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpGet("conversations/unread-sender-count")]
    public async Task<ActionResult<UnreadMessageSenderCountResponse>> CountUnreadMessageSenders(
        CancellationToken cancellationToken)
    {
        var result = await _directConversations.CountUnreadSendersAsync(
            GetCurrentUserIdFromClaims(),
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpGet("conversations/direct/{conversationId:int}/messages")]
    public async Task<ActionResult<IReadOnlyList<CommunityMessageResponse>>> GetDirectMessages(
        int conversationId,
        [FromQuery] int? beforeMessageId,
        [FromQuery] int limit = 8,
        CancellationToken cancellationToken = default)
    {
        var result = await _directConversations.GetDirectMessagesAsync(
            GetCurrentUserIdFromClaims(),
            conversationId,
            beforeMessageId,
            Math.Clamp(limit, 1, 50),
            cancellationToken);

        return ToActionResult(result);
    }

    [EnableRateLimiting(RateLimitPolicies.Messaging)]
    [HttpPost("conversations/direct/{conversationId:int}/messages")]
    public async Task<ActionResult<CommunityMessageResponse>> SendDirectMessage(
        int conversationId,
        [FromBody] SendCommunityMessageRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _directConversations.SendDirectMessageAsync(
            GetCurrentUserIdFromClaims(),
            conversationId,
            request,
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpPost("conversations/direct/{conversationId:int}/read")]
    public async Task<ActionResult<bool>> MarkDirectConversationAsRead(
        int conversationId,
        [FromQuery] int? lastReadMessageId,
        CancellationToken cancellationToken)
    {
        var result = await _directConversations.MarkAsReadAsync(
            GetCurrentUserIdFromClaims(),
            conversationId,
            lastReadMessageId,
            cancellationToken);

        return ToActionResult(result);
    }

    [HttpGet("friends")]
    public async Task<ActionResult<IReadOnlyList<FriendResponse>>> GetFriends(CancellationToken cancellationToken)
    {
        SetCommunityUser();
        return ToActionResult(await _community.GetFriends(cancellationToken));
    }

    [HttpGet("friends/statuses")]
    public async Task<ActionResult<FriendshipStatusesResponse>> GetFriendshipStatuses(
        [FromQuery] string? targetUserIds,
        CancellationToken cancellationToken)
    {
        SetCommunityUser();
        var idList = string.IsNullOrWhiteSpace(targetUserIds)
            ? []
            : targetUserIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, out var id) ? id : 0)
                .Where(id => id > 0)
                .ToList();

        return ToActionResult(await _community.GetFriendshipStatuses(idList, cancellationToken));
    }

    [HttpGet("friends/requests")]
    public async Task<ActionResult<IReadOnlyList<FriendRequestResponse>>> GetFriendRequests(CancellationToken cancellationToken)
    {
        SetCommunityUser();
        return ToActionResult(await _community.GetFriendRequests(cancellationToken));
    }

    [HttpPost("friends/request")]
    public async Task<ActionResult<FriendshipActionResponse>> SendFriendRequest(
        [FromQuery] int targetUserId,
        CancellationToken cancellationToken)
    {
        SetCommunityUser();
        return ToActionResult(await _community.SendFriendRequest(targetUserId, cancellationToken));
    }

    [HttpPost("friends/accept")]
    public async Task<ActionResult<FriendshipActionResponse>> AcceptFriendRequest(
        [FromQuery] int targetUserId,
        CancellationToken cancellationToken)
    {
        SetCommunityUser();
        return ToActionResult(await _community.AcceptFriendRequest(targetUserId, cancellationToken));
    }

    [HttpPost("friends/decline")]
    public async Task<ActionResult<FriendshipActionResponse>> DeclineFriendRequest(
        [FromQuery] int targetUserId,
        CancellationToken cancellationToken)
    {
        SetCommunityUser();
        return ToActionResult(await _community.DeclineFriendRequest(targetUserId, cancellationToken));
    }

    [HttpDelete("friends/{targetUserId:int}")]
    public async Task<ActionResult<FriendshipActionResponse>> RemoveFriend(
        int targetUserId,
        CancellationToken cancellationToken)
    {
        SetCommunityUser();
        return ToActionResult(await _community.RemoveFriend(targetUserId, cancellationToken));
    }
}