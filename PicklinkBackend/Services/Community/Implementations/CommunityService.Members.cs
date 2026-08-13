using Microsoft.EntityFrameworkCore;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services.Community;

namespace PicklinkBackend.Services.Community.Implementations;

public partial class CommunityService
{
    public async Task<CommunityServiceResult<IReadOnlyList<CommunityMemberResponse>>> Members(
        int groupId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var member = await GetMembershipAsync(groupId, userId.Value, cancellationToken);
        var canView = await CanViewGroupAsync(groupId, userId.Value, cancellationToken);
        if (!canView)
        {
            return Forbid();
        }

        var isManager = IsGroupManager(member);
        var membersQuery = _communityRepository.GroupMembers
            .AsNoTracking()
            .Where(groupMember => groupMember.GroupId == groupId);

        if (!isManager)
        {
            membersQuery = membersQuery.Where(groupMember => groupMember.Status == AcceptedStatus);
        }

        var members = await membersQuery
            .OrderByDescending(groupMember => groupMember.Role == OwnerRole)
            .ThenBy(groupMember => groupMember.User.Username)
            .Select(groupMember => new CommunityMemberResponse(
                groupMember.GroupId,
                groupMember.UserId,
                groupMember.User.Username,
                groupMember.User.ProfileImageUrl,
                groupMember.Role,
                groupMember.Status,
                groupMember.JoinedAt,
                groupMember.User.Players
                    .OrderByDescending(player => player.PlayerId)
                    .Select(player => (int?)player.PlayerId)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return Ok(members);
    }

    public async Task<CommunityServiceResult<CommunityMemberResponse>> ApproveMember(
        int groupId,
        int memberUserId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var currentMember = await GetMembershipAsync(groupId, userId.Value, cancellationToken);
        if (!IsGroupManager(currentMember))
        {
            return Forbid();
        }

        var member = await _communityRepository.GroupMembers
            .Include(groupMember => groupMember.User)
            .SingleOrDefaultAsync(groupMember =>
                groupMember.GroupId == groupId &&
                groupMember.UserId == memberUserId,
                cancellationToken);
        if (member is null)
        {
            return NotFound();
        }

        if (member.Status != PendingStatus && member.Status != DeclinedStatus)
        {
            return BadRequest(new { message = "Chỉ có thể duyệt yêu cầu đang chờ." });
        }

        member.Status = AcceptedStatus;
        member.JoinedAt = DateTime.UtcNow;
        var conversation = await EnsureGroupConversationAsync(groupId, cancellationToken);
        await EnsureConversationParticipantAsync(conversation.ConversationId, memberUserId, cancellationToken);
        QueueNotification(memberUserId, "Yêu cầu tham gia câu lạc bộ của bạn đã được duyệt.", $"/clubs/{groupId}");

        await _communityRepository.SaveChangesAsync(cancellationToken);
        _notifications.PublishPending();

        return Ok(new CommunityMemberResponse(
            member.GroupId,
            member.UserId,
            member.User.Username,
            member.User.ProfileImageUrl,
            member.Role,
            member.Status,
            member.JoinedAt));
    }

    public async Task<CommunityServiceResult<CommunityMemberResponse>> DeclineMember(
        int groupId,
        int memberUserId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var currentMember = await GetMembershipAsync(groupId, userId.Value, cancellationToken);
        if (!IsGroupManager(currentMember))
        {
            return Forbid();
        }

        var member = await _communityRepository.GroupMembers
            .Include(groupMember => groupMember.User)
            .SingleOrDefaultAsync(groupMember =>
                groupMember.GroupId == groupId &&
                groupMember.UserId == memberUserId,
                cancellationToken);
        if (member is null)
        {
            return NotFound();
        }

        if (IsOwner(member))
        {
            return BadRequest(new { message = "Không thể từ chối chủ nhóm." });
        }

        if (member.Status != PendingStatus)
        {
            return BadRequest(new { message = "Chỉ có thể từ chối yêu cầu đang chờ." });
        }

        member.Status = DeclinedStatus;
        await RemoveGroupConversationParticipantAsync(groupId, memberUserId, cancellationToken);
        QueueNotification(memberUserId, "Yêu cầu tham gia câu lạc bộ của bạn đã bị từ chối.", $"/clubs/{groupId}");

        await _communityRepository.SaveChangesAsync(cancellationToken);
        _notifications.PublishPending();

        return Ok(new CommunityMemberResponse(
            member.GroupId,
            member.UserId,
            member.User.Username,
            member.User.ProfileImageUrl,
            member.Role,
            member.Status,
            member.JoinedAt));
    }

    public async Task<CommunityServiceResult<CommunityMemberResponse>> BanMember(
        int groupId,
        int memberUserId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var currentMember = await GetMembershipAsync(groupId, userId.Value, cancellationToken);
        if (!IsGroupManager(currentMember))
        {
            return Forbid();
        }

        var member = await _communityRepository.GroupMembers
            .Include(groupMember => groupMember.User)
            .SingleOrDefaultAsync(groupMember =>
                groupMember.GroupId == groupId &&
                groupMember.UserId == memberUserId,
                cancellationToken);
        if (member is null)
        {
            return NotFound();
        }

        if (IsOwner(member))
        {
            return BadRequest(new { message = "Không thể cấm chủ nhóm." });
        }

        if (!CanManageMember(currentMember, member))
        {
            return Forbidden(new { message = "Bạn không thể cấm thành viên có vai trò ngang hoặc cao hơn." });
        }

        member.Status = BannedStatus;
        await RemoveGroupConversationParticipantAsync(groupId, memberUserId, cancellationToken);
        QueueNotification(memberUserId, "Bạn đã bị cấm khỏi câu lạc bộ.", $"/clubs/{groupId}");

        await _communityRepository.SaveChangesAsync(cancellationToken);
        _notifications.PublishPending();

        return Ok(new CommunityMemberResponse(
            member.GroupId,
            member.UserId,
            member.User.Username,
            member.User.ProfileImageUrl,
            member.Role,
            member.Status,
            member.JoinedAt));
    }

    public async Task<CommunityServiceResult> UnbanMember(
        int groupId,
        int memberUserId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var currentMember = await GetMembershipAsync(groupId, userId.Value, cancellationToken);
        if (!IsGroupManager(currentMember))
        {
            return Forbid();
        }

        var member = await _communityRepository.GroupMembers
            .SingleOrDefaultAsync(groupMember =>
                groupMember.GroupId == groupId &&
                groupMember.UserId == memberUserId,
                cancellationToken);
        if (member is null)
        {
            return NotFound();
        }

        if (!string.Equals(member.Status, BannedStatus, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "Thành viên này không bị cấm." });
        }

        if (!CanManageMember(currentMember, member))
        {
            return Forbidden(new { message = "Bạn không thể bỏ cấm thành viên có vai trò ngang hoặc cao hơn." });
        }

        await _communityRepository.RemoveMemberAsync(member, cancellationToken);
        QueueNotification(memberUserId, "Bạn đã được bỏ cấm khỏi câu lạc bộ và có thể gửi lại yêu cầu tham gia.", $"/clubs/{groupId}");

        await _communityRepository.SaveChangesAsync(cancellationToken);
        _notifications.PublishPending();
        return NoContent();
    }

    public async Task<CommunityServiceResult<CommunityMemberResponse>> ChangeMemberRole(
        int groupId,
        int memberUserId,
        ChangeRoleRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var currentMember = await GetMembershipAsync(groupId, userId.Value, cancellationToken);
        if (!IsGroupManager(currentMember))
        {
            return Forbid();
        }

        var allowedRoles = new[] { AdminRole, ModeratorRole, MemberRole };
        var newRole = request.Role?.Trim();
        if (string.IsNullOrEmpty(newRole) ||
            !allowedRoles.Any(r => string.Equals(r, newRole, StringComparison.OrdinalIgnoreCase)))
        {
            return BadRequest(new { message = "Vai trò không hợp lệ." });
        }

        newRole = allowedRoles.First(r => string.Equals(r, newRole, StringComparison.OrdinalIgnoreCase));

        var member = await _communityRepository.GroupMembers
            .Include(groupMember => groupMember.User)
            .SingleOrDefaultAsync(groupMember =>
                groupMember.GroupId == groupId &&
                groupMember.UserId == memberUserId,
                cancellationToken);
        if (member is null)
        {
            return NotFound();
        }

        if (IsOwner(member))
        {
            return BadRequest(new { message = "Không thể thay đổi vai trò của chủ nhóm." });
        }

        if (!CanChangeMemberRole(currentMember, member, newRole))
        {
            return Forbidden(new { message = "Bạn không thể thay đổi thành viên sang vai trò ngang hoặc cao hơn mình." });
        }

        member.Role = newRole;
        await _communityRepository.SaveChangesAsync(cancellationToken);

        return Ok(new CommunityMemberResponse(
            member.GroupId,
            member.UserId,
            member.User.Username,
            member.User.ProfileImageUrl,
            member.Role,
            member.Status,
            member.JoinedAt));
    }

    public async Task<CommunityServiceResult> RemoveMember(
        int groupId,
        int memberUserId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var currentMember = await GetMembershipAsync(groupId, userId.Value, cancellationToken);
        if (!IsGroupManager(currentMember) && userId.Value != memberUserId)
        {
            return Forbid();
        }

        var member = await GetMembershipAsync(groupId, memberUserId, cancellationToken);
        if (member is null)
        {
            return NotFound();
        }

        if (IsOwner(member))
        {
            return BadRequest(new { message = "Không thể xóa chủ nhóm khỏi nhóm." });
        }

        if (userId.Value != memberUserId && !CanManageMember(currentMember, member))
        {
            return Forbidden(new { message = "Bạn không thể xóa thành viên có vai trò ngang hoặc cao hơn." });
        }

        await RemoveGroupConversationParticipantAsync(groupId, memberUserId, cancellationToken);
        await _communityRepository.RemoveMemberAsync(member, cancellationToken);
        await _communityRepository.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
