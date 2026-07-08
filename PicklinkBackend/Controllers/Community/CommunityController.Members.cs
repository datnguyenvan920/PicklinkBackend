using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;

namespace PicklinkBackend.Controllers;

public partial class CommunityController
{
    [HttpGet("groups/{groupId:int}/members")]
    public async Task<ActionResult<IReadOnlyList<CommunityMemberResponse>>> Members(int groupId, CancellationToken cancellationToken)
    {
        SetCommunityUser();
        return ToActionResult(await _community.Members(groupId, cancellationToken));
    }

    [HttpPost("groups/{groupId:int}/members/{memberUserId:int}/approve")]
    public async Task<ActionResult<CommunityMemberResponse>> ApproveMember(int groupId, int memberUserId, CancellationToken cancellationToken)
    {
        SetCommunityUser();
        return ToActionResult(await _community.ApproveMember(groupId, memberUserId, cancellationToken));
    }

    [HttpPost("groups/{groupId:int}/members/{memberUserId:int}/decline")]
    public async Task<ActionResult<CommunityMemberResponse>> DeclineMember(int groupId, int memberUserId, CancellationToken cancellationToken)
    {
        SetCommunityUser();
        return ToActionResult(await _community.DeclineMember(groupId, memberUserId, cancellationToken));
    }

    [HttpPost("groups/{groupId:int}/members/{memberUserId:int}/ban")]
    public async Task<ActionResult<CommunityMemberResponse>> BanMember(int groupId, int memberUserId, CancellationToken cancellationToken)
    {
        SetCommunityUser();
        return ToActionResult(await _community.BanMember(groupId, memberUserId, cancellationToken));
    }

    [HttpPost("groups/{groupId:int}/members/{memberUserId:int}/unban")]
    public async Task<ActionResult> UnbanMember(int groupId, int memberUserId, CancellationToken cancellationToken)
    {
        SetCommunityUser();
        return ToActionResult(await _community.UnbanMember(groupId, memberUserId, cancellationToken));
    }

    [HttpPut("groups/{groupId:int}/members/{memberUserId:int}/role")]
    public async Task<ActionResult<CommunityMemberResponse>> ChangeMemberRole(int groupId, int memberUserId, [FromBody] ChangeRoleRequest request, CancellationToken cancellationToken)
    {
        SetCommunityUser();
        return ToActionResult(await _community.ChangeMemberRole(groupId, memberUserId, request, cancellationToken));
    }

    [HttpDelete("groups/{groupId:int}/members/{memberUserId:int}")]
    public async Task<ActionResult> RemoveMember(int groupId, int memberUserId, CancellationToken cancellationToken)
    {
        SetCommunityUser();
        return ToActionResult(await _community.RemoveMember(groupId, memberUserId, cancellationToken));
    }
}