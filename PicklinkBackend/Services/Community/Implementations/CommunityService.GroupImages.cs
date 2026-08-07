using Microsoft.EntityFrameworkCore;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Services.Community;

namespace PicklinkBackend.Services.Community.Implementations;

public partial class CommunityService
{
    public async Task<CommunityServiceResult<GroupImageResponse>> AddGroupImage(
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
            return BadRequest(new { message = "Vui lòng cung cấp đường dẫn ảnh." });

        var maxSort = await _communityRepository.GroupImages
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
        await _communityRepository.AddGroupImageAsync(image, cancellationToken);
        await _communityRepository.SaveChangesAsync(cancellationToken);

        return Ok(new GroupImageResponse(image.GroupImageId, image.ImageUrl, image.Caption, image.SortOrder));
    }

    public async Task<CommunityServiceResult> RemoveGroupImage(
        int groupId,
        int imageId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var member = await GetMembershipAsync(groupId, userId.Value, cancellationToken);
        if (!IsGroupManager(member)) return Forbid();

        var image = await _communityRepository.GroupImages
            .SingleOrDefaultAsync(i => i.GroupImageId == imageId && i.GroupId == groupId, cancellationToken);
        if (image is null) return NotFound();

        await _communityRepository.RemoveGroupImageAsync(image, cancellationToken);
        await _communityRepository.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
