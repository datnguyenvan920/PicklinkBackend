using System.Data;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;

namespace PicklinkBackend.Services.Community;

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
        var membersQuery = _dbContext.GroupMembers
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
                groupMember.JoinedAt))
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

        var member = await _dbContext.GroupMembers
            .Include(groupMember => groupMember.User)
            .SingleOrDefaultAsync(groupMember =>
                groupMember.GroupId == groupId &&
                groupMember.UserId == memberUserId,
                cancellationToken);
        if (member is null)
        {
            return NotFound();
        }

        member.Status = AcceptedStatus;
        member.JoinedAt = DateTime.UtcNow;
        var conversation = await EnsureGroupConversationAsync(groupId, cancellationToken);
        await EnsureConversationParticipantAsync(conversation.ConversationId, memberUserId, cancellationToken);
        QueueNotification(memberUserId, "Your group join request was approved.");

        await _dbContext.SaveChangesAsync(cancellationToken);
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

        var member = await _dbContext.GroupMembers
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

        member.Status = DeclinedStatus;
        QueueNotification(memberUserId, "Yêu cầu tham gia nhóm của bạn đã bị từ chối.");

        await _dbContext.SaveChangesAsync(cancellationToken);
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

        var member = await _dbContext.GroupMembers
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

        member.Status = BannedStatus;

        // Remove from group conversations
        var conversationParticipants = await _dbContext.ConversationParticipants
            .Where(participant =>
                participant.UserId == memberUserId &&
                participant.Conversation.GroupId == groupId)
            .ToListAsync(cancellationToken);
        _dbContext.ConversationParticipants.RemoveRange(conversationParticipants);

        QueueNotification(memberUserId, "Bạn đã bị cấm khỏi nhóm.");

        await _dbContext.SaveChangesAsync(cancellationToken);
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

        var member = await _dbContext.GroupMembers
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

        _dbContext.GroupMembers.Remove(member);
        QueueNotification(memberUserId, "Bạn đã được bỏ cấm khỏi nhóm. Bạn có thể yêu cầu tham gia lại.");

        await _dbContext.SaveChangesAsync(cancellationToken);
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

        // Validate the requested role
        var allowedRoles = new[] { AdminRole, ModeratorRole, MemberRole };
        var newRole = request.Role?.Trim();
        if (string.IsNullOrEmpty(newRole) ||
            !allowedRoles.Any(r => string.Equals(r, newRole, StringComparison.OrdinalIgnoreCase)))
        {
            return BadRequest(new { message = "Vai trò không hợp lệ." });
        }

        // Normalize the role value
        newRole = allowedRoles.First(r => string.Equals(r, newRole, StringComparison.OrdinalIgnoreCase));

        var member = await _dbContext.GroupMembers
            .Include(groupMember => groupMember.User)
            .SingleOrDefaultAsync(groupMember =>
                groupMember.GroupId == groupId &&
                groupMember.UserId == memberUserId,
                cancellationToken);
        if (member is null)
        {
            return NotFound();
        }

        // Cannot change Owner's role
        if (IsOwner(member))
        {
            return BadRequest(new { message = "Không thể thay đổi vai trò của chủ nhóm." });
        }

        // Only Owner can promote someone to Admin
        if (string.Equals(newRole, AdminRole, StringComparison.OrdinalIgnoreCase) &&
            !IsOwner(currentMember))
        {
            return Forbid();
        }

        member.Role = newRole;
        await _dbContext.SaveChangesAsync(cancellationToken);

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
            return BadRequest(new { message = "Group owner cannot be removed." });
        }

        _dbContext.GroupMembers.Remove(member);
        var conversationParticipants = await _dbContext.ConversationParticipants
            .Where(participant =>
                participant.UserId == memberUserId &&
                participant.Conversation.GroupId == groupId)
            .ToListAsync(cancellationToken);
        _dbContext.ConversationParticipants.RemoveRange(conversationParticipants);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // ── Group Introduction Images ─────────────────────────────────────────

}
