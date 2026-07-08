using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;

namespace PicklinkBackend.Controllers;

public partial class CommunityController
{
    [HttpGet("groups/{groupId:int}/messages")]
    public async Task<ActionResult<IReadOnlyList<CommunityMessageResponse>>> Messages(int groupId, [FromQuery] int? beforeMessageId, [FromQuery] int limit = 50, CancellationToken cancellationToken = default)
    {
        SetCommunityUser();
        return ToActionResult(await _community.Messages(groupId, beforeMessageId, limit, cancellationToken));
    }

    [HttpGet("groups/{groupId:int}/messages/pinned")]
    public async Task<ActionResult<IReadOnlyList<CommunityMessageResponse>>> PinnedMessages(int groupId, CancellationToken cancellationToken)
    {
        SetCommunityUser();
        return ToActionResult(await _community.PinnedMessages(groupId, cancellationToken));
    }

    [HttpPost("groups/{groupId:int}/messages")]
    public async Task<ActionResult<CommunityMessageResponse>> SendMessage(int groupId, SendCommunityMessageRequest request, CancellationToken cancellationToken)
    {
        SetCommunityUser();
        return ToActionResult(await _community.SendMessage(groupId, request, cancellationToken));
    }

    [HttpDelete("groups/{groupId:int}/messages/{messageId:int}")]
    public async Task<ActionResult> DeleteMessage(int groupId, int messageId, CancellationToken cancellationToken)
    {
        SetCommunityUser();
        return ToActionResult(await _community.DeleteMessage(groupId, messageId, cancellationToken));
    }

    [HttpPut("groups/{groupId:int}/messages/{messageId:int}/pin")]
    public async Task<ActionResult<CommunityMessageResponse>> PinMessage(int groupId, int messageId, CancellationToken cancellationToken)
    {
        SetCommunityUser();
        return ToActionResult(await _community.PinMessage(groupId, messageId, true, cancellationToken));
    }
}