using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Services;

namespace PicklinkBackend.Services;

public partial class CommunityService
{
    [HttpPost("groups/{groupId:int}/images")]
    public async Task<ActionResult<GroupImageResponse>> AddGroupImage(
        int groupId,
        AddGroupImageRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var member = await GetMembershipAsync(groupId, userId.Value, cancellationToken);
        if (!IsGroupManager(member)) return Forbid();

        var imageUrl = NormalizeOptional(request.ImageUrl);
        if (imageUrl is null)
            return BadRequest(new { message = "Image URL is required." });

        var maxSort = await _dbContext.GroupImages
            .Where(i => i.GroupId == groupId)
            .MaxAsync(i => (int?)i.SortOrder, cancellationToken) ?? -1;

        var image = new GroupImage
        {
            GroupId = groupId,
            ImageUrl = imageUrl,
            Caption = NormalizeOptional(request.Caption),
            SortOrder = request.SortOrder ?? maxSort + 1,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.GroupImages.Add(image);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new GroupImageResponse(image.GroupImageId, image.ImageUrl, image.Caption, image.SortOrder));
    }

    [HttpDelete("groups/{groupId:int}/images/{imageId:int}")]
    public async Task<ActionResult> RemoveGroupImage(
        int groupId,
        int imageId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var member = await GetMembershipAsync(groupId, userId.Value, cancellationToken);
        if (!IsGroupManager(member)) return Forbid();

        var image = await _dbContext.GroupImages
            .SingleOrDefaultAsync(i => i.GroupImageId == imageId && i.GroupId == groupId, cancellationToken);
        if (image is null) return NotFound();

        _dbContext.GroupImages.Remove(image);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

}
