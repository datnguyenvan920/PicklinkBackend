using Microsoft.EntityFrameworkCore;
using PicklinkBackend.DTOs;

namespace PicklinkBackend.Services;

public partial class CommunityService
{
    public async Task<CommunityServiceResult<IReadOnlyList<FriendResponse>>> GetFriends(
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var friends = await _dbContext.Friendships
            .AsNoTracking()
            .Where(f => (f.RequesterId == userId.Value || f.ReceiverId == userId.Value) && f.Status == "Accepted")
            .Select(f => f.RequesterId == userId.Value ? f.Receiver : f.Requester)
            .Select(u => new FriendResponse(
                u.UserId,
                u.Username,
                u.ProfileImageUrl
            ))
            .ToListAsync(cancellationToken);

        return Ok(friends);
    }
}
