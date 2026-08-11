using Microsoft.EntityFrameworkCore;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Services.Community;
using PicklinkBackend.Services.Notifications;

namespace PicklinkBackend.Services.Community.Implementations;

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

        var friendships = await _communityRepository.Friendships
            .AsNoTracking()
            .Where(f => (f.RequesterId == userId.Value || f.ReceiverId == userId.Value) && f.Status == AcceptedStatus)
            .Include(f => f.Requester).ThenInclude(u => u.Players)
            .Include(f => f.Receiver).ThenInclude(u => u.Players)
            .ToListAsync(cancellationToken);

        var friends = friendships
            .Select(f => f.RequesterId == userId.Value ? f.Receiver : f.Requester)
            .Select(u => new FriendResponse(
                u.UserId,
                u.Username,
                u.ProfileImageUrl,
                u.Players.FirstOrDefault()?.SkillLevel.ToString("0.0")
            ))
            .ToList();

        return Ok(friends);
    }

    public async Task<CommunityServiceResult<IReadOnlyList<PlayerSearchResultResponse>>> SearchPlayers(
        string? query,
        int limit,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var normalizedQuery = query?.Trim().ToLower() ?? string.Empty;
        var takeCount = Math.Clamp(limit, 1, 50);

        var usersQuery = _communityRepository.Users
            .AsNoTracking()
            .Where(u => !u.IsLocked);

        if (userId.HasValue)
        {
            usersQuery = usersQuery.Where(u => u.UserId != userId.Value);
        }

        if (!string.IsNullOrEmpty(normalizedQuery))
        {
            usersQuery = usersQuery.Where(u =>
                u.Username.ToLower().Contains(normalizedQuery) ||
                u.Email.ToLower().Contains(normalizedQuery));
        }

        var matchedUsers = await usersQuery
            .Include(u => u.Players)
            .OrderBy(u => u.Username)
            .Take(takeCount)
            .ToListAsync(cancellationToken);

        var matchedUserIds = matchedUsers.Select(u => u.UserId).ToList();

        var friendshipDict = new Dictionary<int, string>();
        if (userId.HasValue && matchedUserIds.Count > 0)
        {
            var friendships = await _communityRepository.Friendships
                .AsNoTracking()
                .Where(f =>
                    (f.RequesterId == userId.Value && matchedUserIds.Contains(f.ReceiverId)) ||
                    (f.ReceiverId == userId.Value && matchedUserIds.Contains(f.RequesterId)))
                .ToListAsync(cancellationToken);

            foreach (var targetId in matchedUserIds)
            {
                var match = friendships.FirstOrDefault(f =>
                    (f.RequesterId == userId.Value && f.ReceiverId == targetId) ||
                    (f.ReceiverId == userId.Value && f.RequesterId == targetId));

                if (match is null)
                {
                    friendshipDict[targetId] = "None";
                }
                else if (string.Equals(match.Status, AcceptedStatus, StringComparison.OrdinalIgnoreCase))
                {
                    friendshipDict[targetId] = "Accepted";
                }
                else if (string.Equals(match.Status, PendingStatus, StringComparison.OrdinalIgnoreCase))
                {
                    friendshipDict[targetId] = match.RequesterId == userId.Value ? "PendingSent" : "PendingReceived";
                }
                else
                {
                    friendshipDict[targetId] = "None";
                }
            }
        }

        var result = matchedUsers.Select(u => new PlayerSearchResultResponse(
            u.UserId,
            u.Username,
            u.ProfileImageUrl,
            u.Players.FirstOrDefault()?.SkillLevel.ToString("0.0"),
            friendshipDict.GetValueOrDefault(u.UserId, "None")
        )).ToList();

        return Ok(result);
    }

    public async Task<CommunityServiceResult<FriendshipStatusesResponse>> GetFriendshipStatuses(
        IReadOnlyList<int> targetUserIds,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var distinctTargetIds = targetUserIds.Where(id => id > 0 && id != userId.Value).Distinct().ToList();
        var resultDict = new Dictionary<int, string>();

        if (distinctTargetIds.Count == 0)
        {
            return Ok(new FriendshipStatusesResponse(resultDict));
        }

        var friendships = await _communityRepository.Friendships
            .AsNoTracking()
            .Where(f =>
                (f.RequesterId == userId.Value && distinctTargetIds.Contains(f.ReceiverId)) ||
                (f.ReceiverId == userId.Value && distinctTargetIds.Contains(f.RequesterId)))
            .ToListAsync(cancellationToken);

        foreach (var targetId in distinctTargetIds)
        {
            var match = friendships.FirstOrDefault(f =>
                (f.RequesterId == userId.Value && f.ReceiverId == targetId) ||
                (f.ReceiverId == userId.Value && f.RequesterId == targetId));

            if (match is null)
            {
                resultDict[targetId] = "None";
            }
            else if (string.Equals(match.Status, AcceptedStatus, StringComparison.OrdinalIgnoreCase))
            {
                resultDict[targetId] = "Accepted";
            }
            else if (string.Equals(match.Status, PendingStatus, StringComparison.OrdinalIgnoreCase))
            {
                resultDict[targetId] = match.RequesterId == userId.Value ? "PendingSent" : "PendingReceived";
            }
            else
            {
                resultDict[targetId] = "None";
            }
        }

        return Ok(new FriendshipStatusesResponse(resultDict));
    }

    public async Task<CommunityServiceResult<IReadOnlyList<FriendRequestResponse>>> GetFriendRequests(
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var requests = await _communityRepository.Friendships
            .AsNoTracking()
            .Where(f => f.ReceiverId == userId.Value && f.Status == PendingStatus)
            .Include(f => f.Requester).ThenInclude(u => u.Players)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new FriendRequestResponse(
                f.FriendshipId,
                f.RequesterId,
                f.Requester.Username,
                f.Requester.ProfileImageUrl,
                f.Requester.Players.Select(p => (double?)p.SkillLevel).FirstOrDefault() != null
                    ? f.Requester.Players.First().SkillLevel.ToString("0.0")
                    : null,
                f.CreatedAt
            ))
            .ToListAsync(cancellationToken);

        return Ok(requests);
    }

    public async Task<CommunityServiceResult<FriendshipActionResponse>> SendFriendRequest(
        int targetUserId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (targetUserId <= 0 || targetUserId == userId.Value)
        {
            return BadRequest(new { message = "Không thể gửi lời mời kết bạn cho chính mình." });
        }

        var targetUser = await _communityRepository.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.UserId == targetUserId, cancellationToken);
        if (targetUser is null)
        {
            return NotFound(new { message = "Không tìm thấy người dùng." });
        }

        var currentUser = await _communityRepository.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.UserId == userId.Value, cancellationToken);
        var currentUsername = currentUser?.Username ?? "Một người chơi";

        var existing = await _communityRepository.Friendships
            .SingleOrDefaultAsync(f =>
                (f.RequesterId == userId.Value && f.ReceiverId == targetUserId) ||
                (f.ReceiverId == userId.Value && f.RequesterId == targetUserId), cancellationToken);

        if (existing is not null)
        {
            if (string.Equals(existing.Status, AcceptedStatus, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "Hai bạn đã là bạn bè." });
            }

            if (string.Equals(existing.Status, PendingStatus, StringComparison.OrdinalIgnoreCase))
            {
                if (existing.RequesterId == userId.Value)
                {
                    return BadRequest(new { message = "Bạn đã gửi lời mời kết bạn trước đó." });
                }

                // If targetUser already requested, accept the request!
                existing.Status = AcceptedStatus;
                existing.UpdatedAt = DateTime.UtcNow;
                await _communityRepository.SaveChangesAsync(cancellationToken);

                _notifications.Add(new NotificationInput(
                    UserId: targetUserId,
                    Type: NotificationTypes.Club,
                    Title: "Lời mời kết bạn được chấp nhận",
                    Message: $"{currentUsername} đã đồng ý lời mời kết bạn của bạn.",
                    Tone: NotificationTones.Success,
                    LinkTo: "/posts",
                    LinkLabel: "Xem bài viết"));
                _notifications.PublishPending();

                return Ok(new FriendshipActionResponse(targetUserId, "Accepted", "Đã chấp nhận lời mời kết bạn từ đối phương."));
            }

            // Re-open declined/canceled friendship
            existing.RequesterId = userId.Value;
            existing.ReceiverId = targetUserId;
            existing.Status = PendingStatus;
            existing.UpdatedAt = DateTime.UtcNow;
            await _communityRepository.SaveChangesAsync(cancellationToken);
        }
        else
        {
            var newFriendship = new Friendship
            {
                RequesterId = userId.Value,
                ReceiverId = targetUserId,
                Status = PendingStatus,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await _communityRepository.AddFriendshipAsync(newFriendship, cancellationToken);
            await _communityRepository.SaveChangesAsync(cancellationToken);
        }

        _notifications.Add(new NotificationInput(
            UserId: targetUserId,
            Type: NotificationTypes.Club,
            Title: "Lời mời kết bạn mới",
            Message: $"{currentUsername} đã gửi cho bạn một lời mời kết bạn.",
            Tone: NotificationTones.Info,
            LinkTo: "/posts",
            LinkLabel: "Xem lời mời"));
        _notifications.PublishPending();

        return Ok(new FriendshipActionResponse(targetUserId, "PendingSent", "Đã gửi lời mời kết bạn thành công."));
    }

    public async Task<CommunityServiceResult<FriendshipActionResponse>> AcceptFriendRequest(
        int targetUserId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var friendship = await _communityRepository.Friendships
            .SingleOrDefaultAsync(f =>
                f.ReceiverId == userId.Value &&
                f.RequesterId == targetUserId &&
                f.Status == PendingStatus, cancellationToken);

        if (friendship is null)
        {
            return NotFound(new { message = "Không tìm thấy lời mời kết bạn phù hợp." });
        }

        var currentUser = await _communityRepository.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(u => u.UserId == userId.Value, cancellationToken);
        var currentUsername = currentUser?.Username ?? "Một người chơi";

        friendship.Status = AcceptedStatus;
        friendship.UpdatedAt = DateTime.UtcNow;
        await _communityRepository.SaveChangesAsync(cancellationToken);

        _notifications.Add(new NotificationInput(
            UserId: targetUserId,
            Type: NotificationTypes.Club,
            Title: "Lời mời kết bạn được chấp nhận",
            Message: $"{currentUsername} đã đồng ý lời mời kết bạn của bạn.",
            Tone: NotificationTones.Success,
            LinkTo: "/posts",
            LinkLabel: "Xem bài viết"));
        _notifications.PublishPending();

        return Ok(new FriendshipActionResponse(targetUserId, "Accepted", "Đã chấp nhận lời mời kết bạn."));
    }

    public async Task<CommunityServiceResult<FriendshipActionResponse>> DeclineFriendRequest(
        int targetUserId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var friendship = await _communityRepository.Friendships
            .SingleOrDefaultAsync(f =>
                f.ReceiverId == userId.Value &&
                f.RequesterId == targetUserId &&
                f.Status == PendingStatus, cancellationToken);

        if (friendship is null)
        {
            return NotFound(new { message = "Không tìm thấy lời mời kết bạn phù hợp." });
        }

        await _communityRepository.RemoveFriendshipAsync(friendship, cancellationToken);
        await _communityRepository.SaveChangesAsync(cancellationToken);

        return Ok(new FriendshipActionResponse(targetUserId, "None", "Đã từ chối lời mời kết bạn."));
    }

    public async Task<CommunityServiceResult<FriendshipActionResponse>> RemoveFriend(
        int targetUserId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var friendship = await _communityRepository.Friendships
            .SingleOrDefaultAsync(f =>
                (f.RequesterId == userId.Value && f.ReceiverId == targetUserId) ||
                (f.ReceiverId == userId.Value && f.RequesterId == targetUserId), cancellationToken);

        if (friendship is null)
        {
            return NotFound(new { message = "Không tìm thấy mối quan hệ bạn bè." });
        }

        await _communityRepository.RemoveFriendshipAsync(friendship, cancellationToken);
        await _communityRepository.SaveChangesAsync(cancellationToken);

        return Ok(new FriendshipActionResponse(targetUserId, "None", "Đã hủy kết bạn thành công."));
    }
}
