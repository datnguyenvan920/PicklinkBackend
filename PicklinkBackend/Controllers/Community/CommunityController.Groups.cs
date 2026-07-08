using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;

namespace PicklinkBackend.Controllers;

public partial class CommunityController
{
    [AllowAnonymous]
    [HttpGet("groups")]
    public async Task<ActionResult<IReadOnlyList<CommunityGroupResponse>>> Groups(
        [FromQuery] string? query,
        [FromQuery] string? groupType,
        [FromQuery] string? sortBy,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken = default)
    {
        SetCommunityUser();
        return ToActionResult(await _community.Groups(query, groupType, sortBy, page, pageSize, cancellationToken));
    }

    [AllowAnonymous]
    [HttpGet("groups/{groupId:int}")]
    public async Task<ActionResult<CommunityGroupResponse>> Group(int groupId, CancellationToken cancellationToken)
    {
        SetCommunityUser();
        return ToActionResult(await _community.Group(groupId, cancellationToken));
    }

    [HttpPost("groups")]
    public async Task<ActionResult<CommunityGroupResponse>> CreateGroup(CreateCommunityGroupRequest request, CancellationToken cancellationToken)
    {
        SetCommunityUser();
        return ToActionResult(await _community.CreateGroup(request, cancellationToken));
    }

    [HttpPut("groups/{groupId:int}")]
    public async Task<ActionResult<CommunityGroupResponse>> UpdateGroup(int groupId, UpdateCommunityGroupRequest request, CancellationToken cancellationToken)
    {
        SetCommunityUser();
        return ToActionResult(await _community.UpdateGroup(groupId, request, cancellationToken));
    }

    [HttpPost("groups/{groupId:int}/join")]
    public async Task<ActionResult<CommunityGroupResponse>> JoinGroup(int groupId, CancellationToken cancellationToken)
    {
        SetCommunityUser();
        return ToActionResult(await _community.JoinGroup(groupId, cancellationToken));
    }

    [HttpPost("groups/{groupId:int}/leave")]
    public async Task<ActionResult<CommunityGroupResponse>> LeaveGroup(int groupId, CancellationToken cancellationToken)
    {
        SetCommunityUser();
        return ToActionResult(await _community.LeaveGroup(groupId, cancellationToken));
    }
}