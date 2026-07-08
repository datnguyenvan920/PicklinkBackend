using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services;

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

    private ActionResult<T> ToActionResult<T>(DirectConversationServiceResult<T> result) =>
        result.Status switch
        {
            DirectConversationServiceResultStatus.Success => Ok(result.Value),
            DirectConversationServiceResultStatus.BadRequest => BadRequest(new { message = result.ErrorMessage }),
            DirectConversationServiceResultStatus.Unauthorized => Unauthorized(),
            DirectConversationServiceResultStatus.Forbidden => Forbid(),
            DirectConversationServiceResultStatus.NotFound => NotFound(new { message = result.ErrorMessage }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };

    private int? GetCurrentUserIdFromClaims()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out var userId) ? userId : null;
    }
}
