using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;

namespace PicklinkBackend.Controllers;

public partial class CommunityController
{
    [HttpPost("groups/{groupId:int}/images")]
    public async Task<ActionResult<GroupImageResponse>> AddGroupImage(int groupId, AddGroupImageRequest request, CancellationToken cancellationToken)
    {
        SetCommunityUser();
        return ToActionResult(await _community.AddGroupImage(groupId, request, cancellationToken));
    }

    [HttpDelete("groups/{groupId:int}/images/{imageId:int}")]
    public async Task<ActionResult> RemoveGroupImage(int groupId, int imageId, CancellationToken cancellationToken)
    {
        SetCommunityUser();
        return ToActionResult(await _community.RemoveGroupImage(groupId, imageId, cancellationToken));
    }
}