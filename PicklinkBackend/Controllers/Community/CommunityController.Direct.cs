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

    [HttpGet("friends")]
    public async Task<ActionResult<IReadOnlyList<FriendResponse>>> GetFriends(CancellationToken cancellationToken)
    {
        SetCommunityUser();
        return ToActionResult(await _community.GetFriends(cancellationToken));
    }
}