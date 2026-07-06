using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Services;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public partial class CommunityController : ControllerBase
{
    private const string PublicGroup = "Public";
    private const string PrivateGroup = "Private";
    private const string AcceptedStatus = "Accepted";
    private const string PendingStatus = "Pending";
    private const string DeclinedStatus = "Declined";
    private const string BannedStatus = "Banned";
    private const string OwnerRole = "Owner";
    private const string AdminRole = "Admin";
    private const string ModeratorRole = "Moderator";
    private const string MemberRole = "Member";

    private readonly ApplicationDbContext _dbContext;
    private readonly NotificationService _notifications;

    public CommunityController(
        ApplicationDbContext dbContext,
        NotificationService notifications)
    {
        _dbContext = dbContext;
        _notifications = notifications;
    }

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
        var userId = GetCurrentUserId();

        var groupsQuery = _dbContext.SocialGroups.AsNoTracking();

        // 1. Search filter
        if (!string.IsNullOrWhiteSpace(query))
        {
            var tokens = query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                              .Select(t => t.Trim())
                              .Where(t => t.Length > 0)
                              .ToList();

            var hanoiTokens = new[] { "hà", "nội", "ha", "noi" };
            var hcmTokens = new[] { "tp.", "hồ", "chí", "minh", "ho", "chi" };
            var danangTokens = new[] { "đà", "nẵng", "da", "nang" };

            var queryTokens = tokens.Select(t => t.ToLower()).ToList();

            bool hasHanoi = queryTokens.Any(t => hanoiTokens.Contains(t));
            bool hasHcm = queryTokens.Any(t => hcmTokens.Contains(t));
            bool hasDanang = queryTokens.Any(t => danangTokens.Contains(t));

            var normalTokens = tokens.Where(t => 
                !hanoiTokens.Contains(t.ToLower()) && 
                !hcmTokens.Contains(t.ToLower()) && 
                !danangTokens.Contains(t.ToLower())
            ).ToList();

            foreach (var token in normalTokens)
            {
                groupsQuery = groupsQuery.Where(group =>
                    EF.Functions.Like(group.GroupName, $"%{token}%") ||
                    (group.Description != null && EF.Functions.Like(group.Description, $"%{token}%")));
            }

            if (hasHanoi)
            {
                groupsQuery = groupsQuery.Where(group =>
                    EF.Functions.Like(group.GroupName, "%hà nội%") ||
                    EF.Functions.Like(group.GroupName, "%ha noi%") ||
                    (group.Description != null && (
                        EF.Functions.Like(group.Description, "%hà nội%") ||
                        EF.Functions.Like(group.Description, "%ha noi%")
                    ))
                );
            }
            if (hasHcm)
            {
                groupsQuery = groupsQuery.Where(group =>
                    EF.Functions.Like(group.GroupName, "%hồ chí minh%") ||
                    EF.Functions.Like(group.GroupName, "%ho chi minh%") ||
                    EF.Functions.Like(group.GroupName, "%tp.hcm%") ||
                    EF.Functions.Like(group.GroupName, "%tphcm%") ||
                    (group.Description != null && (
                        EF.Functions.Like(group.Description, "%hồ chí minh%") ||
                        EF.Functions.Like(group.Description, "%ho chi minh%") ||
                        EF.Functions.Like(group.Description, "%tp.hcm%") ||
                        EF.Functions.Like(group.Description, "%tphcm%")
                    ))
                );
            }
            if (hasDanang)
            {
                groupsQuery = groupsQuery.Where(group =>
                    EF.Functions.Like(group.GroupName, "%đà nẵng%") ||
                    EF.Functions.Like(group.GroupName, "%da nang%") ||
                    (group.Description != null && (
                        EF.Functions.Like(group.Description, "%đà nẵng%") ||
                        EF.Functions.Like(group.Description, "%da nang%")
                    ))
                );
            }
        }

        // 2. Group type filter
        if (string.Equals(groupType, "Mine", StringComparison.OrdinalIgnoreCase))
        {
            if (userId == null)
            {
                return Unauthorized(new { message = "You must be logged in to view your groups." });
            }
            groupsQuery = groupsQuery.Where(group =>
                group.GroupMembers.Any(member =>
                    member.UserId == userId.Value &&
                    (member.Status == AcceptedStatus || member.Role == OwnerRole)));
        }
        else if (string.Equals(groupType, "Public", StringComparison.OrdinalIgnoreCase))
        {
            groupsQuery = groupsQuery.Where(group => group.GroupType == PublicGroup);
        }
        else if (string.Equals(groupType, "Private", StringComparison.OrdinalIgnoreCase))
        {
            groupsQuery = groupsQuery.Where(group => group.GroupType == PrivateGroup);
        }

        // 3. Sorting
        if (string.Equals(sortBy, "members", StringComparison.OrdinalIgnoreCase))
        {
            groupsQuery = groupsQuery.OrderByDescending(group => group.GroupMembers.Count(member => member.Status == AcceptedStatus));
        }
        else if (string.Equals(sortBy, "active", StringComparison.OrdinalIgnoreCase))
        {
            groupsQuery = groupsQuery.OrderByDescending(group =>
                group.Posts.Count +
                _dbContext.Messages.Count(message =>
                    message.Conversation.GroupId == group.GroupId &&
                    !message.IsDeleted));
        }
        else
        {
            // default is newest
            groupsQuery = groupsQuery.OrderByDescending(group => group.CreatedAt);
        }

        // 4. Pagination
        var queryable = groupsQuery;
        if (page.HasValue && pageSize.HasValue)
        {
            queryable = queryable.Skip((page.Value - 1) * pageSize.Value).Take(pageSize.Value);
        }

        var groups = await queryable
            .Select(group => new CommunityGroupResponse(
                group.GroupId,
                group.GroupName,
                group.Description,
                group.GroupType,
                group.CoverImageUrl,
                group.CreatedAt,
                group.OwnerId,
                group.Owner.User.Username,
                group.GroupMembers.Count(member => member.Status == AcceptedStatus),
                userId.HasValue
                    ? group.GroupMembers
                        .Where(member => member.UserId == userId.Value)
                        .Select(member => member.Role)
                        .FirstOrDefault()
                    : null,
                userId.HasValue
                    ? group.GroupMembers
                        .Where(member => member.UserId == userId.Value)
                        .Select(member => member.Status)
                        .FirstOrDefault()
                    : null,
                group.Posts.Count,
                _dbContext.Messages.Count(message =>
                    message.Conversation.GroupId == group.GroupId &&
                    !message.IsDeleted),
                group.Rules,
                group.OverallRating,
                group.RatingCount,
                new List<GroupImageResponse>(),
                group.ActiveLocation))
            .ToListAsync(cancellationToken);

        return Ok(groups);
    }

    [AllowAnonymous]
    [HttpGet("groups/{groupId:int}")]
    public async Task<ActionResult<CommunityGroupResponse>> Group(
        int groupId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var group = await BuildGroupResponseAsync(groupId, userId, cancellationToken);
        if (group is null)
        {
            return NotFound();
        }

        return Ok(group);
    }

    [HttpPost("groups")]
    public async Task<ActionResult<CommunityGroupResponse>> CreateGroup(
        CreateCommunityGroupRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var groupName = NormalizeRequired(request.GroupName);
        if (groupName is null)
        {
            return BadRequest(new { message = "Group name is required." });
        }

        var owner = await GetOrCreatePlayerAsync(userId.Value, cancellationToken);
        var now = DateTime.UtcNow;
        var group = new SocialGroup
        {
            Owner = owner,
            GroupName = groupName,
            Description = NormalizeOptional(request.Description),
            GroupType = NormalizeGroupType(request.GroupType),
            CoverImageUrl = NormalizeOptional(request.CoverImageUrl),
            ActiveLocation = NormalizeOptional(request.ActiveLocation),
            CreatedAt = now
        };

        _dbContext.SocialGroups.Add(group);
        _dbContext.GroupMembers.Add(new GroupMember
        {
            Group = group,
            UserId = userId.Value,
            Role = OwnerRole,
            Status = AcceptedStatus,
            JoinedAt = now
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        await EnsureGroupConversationAsync(group.GroupId, cancellationToken);

        var response = await BuildGroupResponseAsync(group.GroupId, userId.Value, cancellationToken);
        return CreatedAtAction(nameof(Group), new { groupId = group.GroupId }, response);
    }

    [HttpPut("groups/{groupId:int}")]
    public async Task<ActionResult<CommunityGroupResponse>> UpdateGroup(
        int groupId,
        UpdateCommunityGroupRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var member = await GetMembershipAsync(groupId, userId.Value, cancellationToken);
        if (!IsGroupManager(member))
        {
            return Forbid();
        }

        var group = await _dbContext.SocialGroups
            .SingleOrDefaultAsync(group => group.GroupId == groupId, cancellationToken);
        if (group is null)
        {
            return NotFound();
        }

        var groupName = NormalizeOptional(request.GroupName);
        if (groupName is not null)
        {
            group.GroupName = groupName;
        }

        if (request.Description is not null)
        {
            group.Description = NormalizeOptional(request.Description);
        }

        if (request.ActiveLocation is not null)
        {
            group.ActiveLocation = NormalizeOptional(request.ActiveLocation);
        }

        if (request.GroupType is not null)
        {
            group.GroupType = NormalizeGroupType(request.GroupType);
        }

        if (request.CoverImageUrl is not null)
        {
            group.CoverImageUrl = NormalizeOptional(request.CoverImageUrl);
        }

        if (request.Rules is not null)
        {
            group.Rules = string.IsNullOrWhiteSpace(request.Rules) ? null : request.Rules.Trim();
        }

        if (request.OverallRating is not null)
        {
            group.OverallRating = Math.Max(0, Math.Min(5, request.OverallRating.Value));
        }

        if (request.RatingCount is not null)
        {
            group.RatingCount = Math.Max(0, request.RatingCount.Value);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = await BuildGroupResponseAsync(groupId, userId.Value, cancellationToken);
        return Ok(response);
    }

    [HttpPost("groups/{groupId:int}/join")]
    public async Task<ActionResult<CommunityGroupResponse>> JoinGroup(
        int groupId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var group = await _dbContext.SocialGroups
            .SingleOrDefaultAsync(group => group.GroupId == groupId, cancellationToken);
        if (group is null)
        {
            return NotFound();
        }

        var status = string.Equals(group.GroupType, PrivateGroup, StringComparison.OrdinalIgnoreCase)
            ? PendingStatus
            : AcceptedStatus;

        var member = await _dbContext.GroupMembers
            .SingleOrDefaultAsync(member => member.GroupId == groupId && member.UserId == userId.Value, cancellationToken);

        if (member is not null &&
            string.Equals(member.Status, BannedStatus, StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(403, new { message = "Bạn đã bị cấm khỏi nhóm này." });
        }

        if (member is null)
        {
            member = new GroupMember
            {
                GroupId = groupId,
                UserId = userId.Value,
                Role = MemberRole,
                Status = status,
                JoinedAt = DateTime.UtcNow
            };
            _dbContext.GroupMembers.Add(member);
        }
        else if (string.Equals(member.Status, DeclinedStatus, StringComparison.OrdinalIgnoreCase))
        {
            member.Status = status;
            member.Role = MemberRole;
            member.JoinedAt = DateTime.UtcNow;
        }
        else if (string.Equals(member.Status, PendingStatus, StringComparison.OrdinalIgnoreCase))
        {
            // Already pending, do nothing
        }
        else if (!string.Equals(member.Status, AcceptedStatus, StringComparison.OrdinalIgnoreCase))
        {
            member.Status = status;
            member.JoinedAt = DateTime.UtcNow;
        }

        if (status == PendingStatus)
        {
            await NotifyGroupManagersAsync(groupId, userId.Value, "A new member requested to join your group.", cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _notifications.PublishPending();

        if (status == AcceptedStatus)
        {
            var conversation = await EnsureGroupConversationAsync(groupId, cancellationToken);
            await EnsureConversationParticipantAsync(conversation.ConversationId, userId.Value, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var response = await BuildGroupResponseAsync(groupId, userId.Value, cancellationToken);
        return Ok(response);
    }

    [HttpPost("groups/{groupId:int}/leave")]
    public async Task<ActionResult<CommunityGroupResponse>> LeaveGroup(
        int groupId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var member = await GetMembershipAsync(groupId, userId.Value, cancellationToken);
        if (member is null)
        {
            return NotFound();
        }

        if (IsOwner(member))
        {
            return BadRequest(new { message = "Group owner cannot leave the group." });
        }

        _dbContext.GroupMembers.Remove(member);
        var conversationParticipants = await _dbContext.ConversationParticipants
            .Where(participant =>
                participant.UserId == userId.Value &&
                participant.Conversation.GroupId == groupId)
            .ToListAsync(cancellationToken);
        _dbContext.ConversationParticipants.RemoveRange(conversationParticipants);

        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = await BuildGroupResponseAsync(groupId, userId.Value, cancellationToken);
        return Ok(response);
    }

    [HttpGet("groups/{groupId:int}/members")]
    public async Task<ActionResult<IReadOnlyList<CommunityMemberResponse>>> Members(
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

    [HttpPost("groups/{groupId:int}/members/{memberUserId:int}/approve")]
    public async Task<ActionResult<CommunityMemberResponse>> ApproveMember(
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

    [HttpPost("groups/{groupId:int}/members/{memberUserId:int}/decline")]
    public async Task<ActionResult<CommunityMemberResponse>> DeclineMember(
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

    [HttpPost("groups/{groupId:int}/members/{memberUserId:int}/ban")]
    public async Task<ActionResult<CommunityMemberResponse>> BanMember(
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

    [HttpPost("groups/{groupId:int}/members/{memberUserId:int}/unban")]
    public async Task<ActionResult> UnbanMember(
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

    [HttpPut("groups/{groupId:int}/members/{memberUserId:int}/role")]
    public async Task<ActionResult<CommunityMemberResponse>> ChangeMemberRole(
        int groupId,
        int memberUserId,
        [FromBody] ChangeRoleRequest request,
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

    [HttpDelete("groups/{groupId:int}/members/{memberUserId:int}")]
    public async Task<ActionResult> RemoveMember(
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

    [HttpGet("groups/{groupId:int}/posts")]
    public async Task<ActionResult<IReadOnlyList<CommunityPostResponse>>> Posts(
        int groupId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (!await CanViewGroupAsync(groupId, userId.Value, cancellationToken))
        {
            return Forbid();
        }

        var member = await GetMembershipAsync(groupId, userId.Value, cancellationToken);
        var isManager = IsGroupManager(member);

        var postsQuery = _dbContext.Posts
            .AsNoTracking()
            .Where(post => post.GroupId == groupId);

        if (!isManager)
        {
            // Regular members only see approved posts and their own pending posts
            postsQuery = postsQuery.Where(post =>
                post.Visibility != PendingStatus || post.AuthorId == userId.Value);
        }

        var posts = await postsQuery
            .OrderByDescending(post => post.CreatedAt)
            .Take(100)
            .Select(post => new CommunityPostResponse(
                post.PostId,
                post.GroupId,
                post.AuthorId,
                post.Author.Username,
                post.Author.ProfileImageUrl,
                post.Content,
                post.PostType,
                post.Visibility,
                post.CreatedAt,
                post.UpdatedAt,
                post.PostMedia
                    .OrderBy(media => media.DisplayOrder)
                    .Select(media => media.MediaUrl)
                    .ToList(),
                post.PostLikes.Count,
                post.PostComments.Count,
                post.PostLikes.Any(like => like.UserId == userId.Value),
                post.PostLikes
                    .Where(like => like.UserId == userId.Value)
                    .Select(like => like.ReactionType)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return Ok(posts);
    }

    [HttpPost("groups/{groupId:int}/posts")]
    public async Task<ActionResult<CommunityPostResponse>> CreatePost(
        int groupId,
        CreateCommunityPostRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        if (!await CanInteractWithGroupAsync(groupId, userId.Value, cancellationToken))
        {
            return Forbid();
        }

        var content = NormalizeOptional(request.Content);
        var mediaUrls = NormalizeMediaUrls(request.MediaUrls);
        if (content is null && mediaUrls.Count == 0)
        {
            return BadRequest(new { message = "Post content or media is required." });
        }

        var now = DateTime.UtcNow;
        var post = new Post
        {
            GroupId = groupId,
            AuthorId = userId.Value,
            Content = content,
            PostType = "GroupPost",
            Visibility = PendingStatus,
            CreatedAt = now,
            UpdatedAt = now
        };

        for (var index = 0; index < mediaUrls.Count; index++)
        {
            post.PostMedia.Add(new PostMedia
            {
                MediaUrl = mediaUrls[index],
                MediaType = "Image",
                DisplayOrder = index
            });
        }

        _dbContext.Posts.Add(post);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = await BuildPostResponseAsync(post.PostId, userId.Value, cancellationToken);
        return CreatedAtAction(nameof(Posts), new { groupId }, response);
    }

    [HttpGet("posts")]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<CommunityPostResponse>>> GetCommunityPosts(
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();

        var postsQuery = _dbContext.Posts
            .AsNoTracking()
            .Where(post =>
                (post.GroupId == null || (post.Group != null && post.Group.GroupType == PublicGroup))
                && post.Visibility != PendingStatus)
            .OrderByDescending(post => post.CreatedAt)
            .Take(100);

        var posts = await postsQuery
            .Select(post => new CommunityPostResponse(
                post.PostId,
                post.GroupId,
                post.AuthorId,
                post.Author.Username,
                post.Author.ProfileImageUrl,
                post.Content,
                post.PostType,
                post.Visibility,
                post.CreatedAt,
                post.UpdatedAt,
                post.PostMedia
                    .OrderBy(media => media.DisplayOrder)
                    .Select(media => media.MediaUrl)
                    .ToList(),
                post.PostLikes.Count,
                post.PostComments.Count,
                userId.HasValue ? post.PostLikes.Any(like => like.UserId == userId.Value) : false,
                userId.HasValue
                    ? post.PostLikes
                        .Where(like => like.UserId == userId.Value)
                        .Select(like => like.ReactionType)
                        .FirstOrDefault()
                    : null))
            .ToListAsync(cancellationToken);

        return Ok(posts);
    }

    [HttpPost("posts")]
    public async Task<ActionResult<CommunityPostResponse>> CreateCommunityPost(
        CreateCommunityPostRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var content = NormalizeOptional(request.Content);
        var mediaUrls = NormalizeMediaUrls(request.MediaUrls);
        if (content is null && mediaUrls.Count == 0)
        {
            return BadRequest(new { message = "Nội dung bài viết hoặc hình ảnh là bắt buộc." });
        }

        var now = DateTime.UtcNow;
        var post = new Post
        {
            GroupId = null,
            AuthorId = userId.Value,
            Content = content,
            PostType = "Post",
            Visibility = "Public",
            CreatedAt = now,
            UpdatedAt = now
        };

        for (var index = 0; index < mediaUrls.Count; index++)
        {
            post.PostMedia.Add(new PostMedia
            {
                MediaUrl = mediaUrls[index],
                MediaType = "Image",
                DisplayOrder = index
            });
        }

        _dbContext.Posts.Add(post);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = await BuildPostResponseAsync(post.PostId, userId.Value, cancellationToken);
        return CreatedAtAction(nameof(GetCommunityPosts), null, response);
    }

    [HttpGet("posts/{postId:int}")]
    [AllowAnonymous]
    public async Task<ActionResult<CommunityPostResponse>> GetPost(
        int postId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var post = await _dbContext.Posts
            .SingleOrDefaultAsync(p => p.PostId == postId, cancellationToken);

        if (post is null)
        {
            return NotFound();
        }

        if (post.GroupId is not null &&
            !await CanViewGroupAsync(post.GroupId.Value, userId ?? 0, cancellationToken))
        {
            return Forbid();
        }

        var response = await BuildPostResponseAsync(postId, userId ?? 0, cancellationToken);
        return Ok(response);
    }

    [HttpPut("posts/{postId:int}")]
    public async Task<ActionResult<CommunityPostResponse>> UpdatePost(
        int postId,
        UpdateCommunityPostRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var post = await _dbContext.Posts
            .SingleOrDefaultAsync(post => post.PostId == postId, cancellationToken);
        if (post is null)
        {
            return NotFound();
        }

        if (!await CanManagePostAsync(post, userId.Value, cancellationToken))
        {
            return Forbid();
        }

        var content = NormalizeOptional(request.Content);
        if (content is null)
        {
            return BadRequest(new { message = "Post content is required." });
        }

        post.Content = content;
        post.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        _notifications.PublishPending();

        var response = await BuildPostResponseAsync(postId, userId.Value, cancellationToken);
        return Ok(response);
    }

    [HttpDelete("posts/{postId:int}")]
    public async Task<ActionResult> DeletePost(
        int postId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var post = await _dbContext.Posts
            .Include(post => post.PostComments)
            .Include(post => post.PostLikes)
            .Include(post => post.PostMedia)
            .SingleOrDefaultAsync(post => post.PostId == postId, cancellationToken);
        if (post is null)
        {
            return NotFound();
        }

        if (!await CanManagePostAsync(post, userId.Value, cancellationToken))
        {
            return Forbid();
        }

        _dbContext.PostComments.RemoveRange(post.PostComments);
        _dbContext.PostLikes.RemoveRange(post.PostLikes);
        _dbContext.PostMedia.RemoveRange(post.PostMedia);
        _dbContext.Posts.Remove(post);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpPost("posts/{postId:int}/approve")]
    public async Task<ActionResult<CommunityPostResponse>> ApprovePost(
        int postId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var post = await _dbContext.Posts
            .SingleOrDefaultAsync(post => post.PostId == postId, cancellationToken);
        if (post is null)
        {
            return NotFound();
        }

        if (post.GroupId is null)
        {
            return BadRequest(new { message = "Only group posts can be approved." });
        }

        var member = await GetMembershipAsync(post.GroupId.Value, userId.Value, cancellationToken);
        if (!IsGroupManager(member))
        {
            return Forbid();
        }

        post.Visibility = "Public";
        post.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = await BuildPostResponseAsync(postId, userId.Value, cancellationToken);
        return Ok(response);
    }

    [HttpPost("posts/{postId:int}/reaction")]
    public async Task<ActionResult<CommunityPostResponse>> ReactToPost(
        int postId,
        ReactToPostRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var post = await _dbContext.Posts
            .SingleOrDefaultAsync(post => post.PostId == postId, cancellationToken);
        if (post is null)
        {
            return NotFound();
        }

        if (post.GroupId is not null &&
            !await CanInteractWithGroupAsync(post.GroupId.Value, userId.Value, cancellationToken))
        {
            return Forbid();
        }

        var reactionType = NormalizeOptional(request.ReactionType) ?? "Like";
        var existingLike = await _dbContext.PostLikes
            .SingleOrDefaultAsync(like => like.PostId == postId && like.UserId == userId.Value, cancellationToken);

        if (existingLike is null)
        {
            existingLike = new PostLike
            {
                PostId = postId,
                UserId = userId.Value,
                ReactionType = reactionType,
                CreatedAt = DateTime.UtcNow
            };
            _dbContext.PostLikes.Add(existingLike);
        }
        else
        {
            existingLike.ReactionType = reactionType;
        }

        if (post.AuthorId != userId.Value)
        {
            QueueNotification(post.AuthorId, "Someone reacted to your post.");
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = await BuildPostResponseAsync(postId, userId.Value, cancellationToken);
        return Ok(response);
    }

    [HttpDelete("posts/{postId:int}/reaction")]
    public async Task<ActionResult<CommunityPostResponse>> RemoveReaction(
        int postId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var post = await _dbContext.Posts
            .SingleOrDefaultAsync(post => post.PostId == postId, cancellationToken);
        if (post is null)
        {
            return NotFound();
        }

        var existingLike = await _dbContext.PostLikes
            .SingleOrDefaultAsync(like => like.PostId == postId && like.UserId == userId.Value, cancellationToken);
        if (existingLike is not null)
        {
            _dbContext.PostLikes.Remove(existingLike);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var response = await BuildPostResponseAsync(postId, userId.Value, cancellationToken);
        return Ok(response);
    }

    [HttpGet("posts/{postId:int}/comments")]
    public async Task<ActionResult<IReadOnlyList<CommunityCommentResponse>>> Comments(
        int postId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var post = await _dbContext.Posts
            .AsNoTracking()
            .SingleOrDefaultAsync(post => post.PostId == postId, cancellationToken);
        if (post is null)
        {
            return NotFound();
        }

        if (post.GroupId is not null &&
            !await CanViewGroupAsync(post.GroupId.Value, userId.Value, cancellationToken))
        {
            return Forbid();
        }

        var comments = await _dbContext.PostComments
            .AsNoTracking()
            .Where(comment => comment.PostId == postId)
            .OrderBy(comment => comment.CreatedAt)
            .Select(comment => new {
                comment.CommentId,
                comment.PostId,
                comment.UserId,
                Username = comment.User.Username,
                ProfileImageUrl = comment.User.ProfileImageUrl,
                comment.ParentCommentId,
                comment.Content,
                comment.CreatedAt,
                comment.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        var responses = new List<CommunityCommentResponse>();
        foreach (var c in comments)
        {
            var likeCount = await GetCommentLikeCountAsync(c.CommentId, cancellationToken);
            var likedByMe = await IsCommentLikedByMeAsync(c.CommentId, userId.Value, cancellationToken);
            responses.Add(new CommunityCommentResponse(
                c.CommentId,
                c.PostId,
                c.UserId,
                c.Username,
                c.ProfileImageUrl,
                c.ParentCommentId,
                c.Content,
                c.CreatedAt,
                c.UpdatedAt,
                likeCount,
                likedByMe
            ));
        }

        return Ok(responses);
    }

    [HttpPost("posts/{postId:int}/comments")]
    public async Task<ActionResult<CommunityCommentResponse>> CreateComment(
        int postId,
        CreateCommunityCommentRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var content = NormalizeRequired(request.Content);
        if (content is null)
        {
            return BadRequest(new { message = "Comment content is required." });
        }

        var post = await _dbContext.Posts
            .SingleOrDefaultAsync(post => post.PostId == postId, cancellationToken);
        if (post is null)
        {
            return NotFound();
        }

        if (post.GroupId is not null &&
            !await CanInteractWithGroupAsync(post.GroupId.Value, userId.Value, cancellationToken))
        {
            return Forbid();
        }

        if (request.ParentCommentId is not null)
        {
            var parentExists = await _dbContext.PostComments
                .AnyAsync(comment =>
                    comment.CommentId == request.ParentCommentId.Value &&
                    comment.PostId == postId,
                    cancellationToken);
            if (!parentExists)
            {
                return BadRequest(new { message = "Parent comment was not found." });
            }
        }

        var now = DateTime.UtcNow;
        var comment = new PostComment
        {
            PostId = postId,
            UserId = userId.Value,
            ParentCommentId = request.ParentCommentId,
            Content = content,
            CreatedAt = now,
            UpdatedAt = now
        };

        _dbContext.PostComments.Add(comment);
        if (post.AuthorId != userId.Value)
        {
            QueueNotification(post.AuthorId, "Someone commented on your post.");
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _notifications.PublishPending();

        var response = await BuildCommentResponseAsync(comment.CommentId, cancellationToken);
        return CreatedAtAction(nameof(Comments), new { postId }, response);
    }

    [HttpPut("comments/{commentId:int}")]
    public async Task<ActionResult<CommunityCommentResponse>> UpdateComment(
        int commentId,
        UpdateCommunityCommentRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var content = NormalizeRequired(request.Content);
        if (content is null)
        {
            return BadRequest(new { message = "Comment content is required." });
        }

        var comment = await _dbContext.PostComments
            .Include(comment => comment.Post)
            .SingleOrDefaultAsync(comment => comment.CommentId == commentId, cancellationToken);
        if (comment is null)
        {
            return NotFound();
        }

        if (!await CanManageCommentAsync(comment, userId.Value, cancellationToken))
        {
            return Forbid();
        }

        comment.Content = content;
        comment.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = await BuildCommentResponseAsync(commentId, cancellationToken);
        return Ok(response);
    }

    [HttpDelete("comments/{commentId:int}")]
    public async Task<ActionResult> DeleteComment(
        int commentId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var comment = await _dbContext.PostComments
            .Include(comment => comment.Post)
            .SingleOrDefaultAsync(comment => comment.CommentId == commentId, cancellationToken);
        if (comment is null)
        {
            return NotFound();
        }

        if (!await CanManageCommentAsync(comment, userId.Value, cancellationToken))
        {
            return Forbid();
        }

        _dbContext.PostComments.Remove(comment);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private async Task<CommunityGroupResponse?> BuildGroupResponseAsync(
        int groupId,
        int? userId,
        CancellationToken cancellationToken)
    {
        var group = await _dbContext.SocialGroups
            .AsNoTracking()
            .Include(g => g.GroupImages)
            .Where(g => g.GroupId == groupId)
            .Select(g => new
            {
                g.GroupId,
                g.GroupName,
                g.Description,
                g.GroupType,
                g.CoverImageUrl,
                g.CreatedAt,
                g.OwnerId,
                OwnerName = g.Owner.User.Username,
                MemberCount = g.GroupMembers.Count(m => m.Status == AcceptedStatus),
                MyRole = userId.HasValue
                    ? g.GroupMembers
                        .Where(m => m.UserId == userId.Value)
                        .Select(m => m.Role)
                        .FirstOrDefault()
                    : null,
                MyStatus = userId.HasValue
                    ? g.GroupMembers
                        .Where(m => m.UserId == userId.Value)
                        .Select(m => m.Status)
                        .FirstOrDefault()
                    : null,
                PostCount = g.Posts.Count,
                MessageCount = _dbContext.Messages.Count(message =>
                    message.Conversation.GroupId == g.GroupId && !message.IsDeleted),
                g.Rules,
                g.OverallRating,
                g.RatingCount,
                g.ActiveLocation,
                Images = g.GroupImages.OrderBy(i => i.SortOrder).ThenBy(i => i.GroupImageId)
                    .Select(i => new GroupImageResponse(i.GroupImageId, i.ImageUrl, i.Caption, i.SortOrder))
                    .ToList()
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (group is null) return null;

        return new CommunityGroupResponse(
            group.GroupId,
            group.GroupName,
            group.Description,
            group.GroupType,
            group.CoverImageUrl,
            group.CreatedAt,
            group.OwnerId,
            group.OwnerName,
            group.MemberCount,
            group.MyRole,
            group.MyStatus,
            group.PostCount,
            group.MessageCount,
            group.Rules,
            group.OverallRating,
            group.RatingCount,
            group.Images,
            group.ActiveLocation);
    }

    private async Task<CommunityPostResponse> BuildPostResponseAsync(
        int postId,
        int userId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Posts
            .AsNoTracking()
            .Where(post => post.PostId == postId)
            .Select(post => new CommunityPostResponse(
                post.PostId,
                post.GroupId,
                post.AuthorId,
                post.Author.Username,
                post.Author.ProfileImageUrl,
                post.Content,
                post.PostType,
                post.Visibility,
                post.CreatedAt,
                post.UpdatedAt,
                post.PostMedia
                    .OrderBy(media => media.DisplayOrder)
                    .Select(media => media.MediaUrl)
                    .ToList(),
                post.PostLikes.Count,
                post.PostComments.Count,
                post.PostLikes.Any(like => like.UserId == userId),
                post.PostLikes
                    .Where(like => like.UserId == userId)
                    .Select(like => like.ReactionType)
                    .FirstOrDefault()))
            .SingleAsync(cancellationToken);
    }

    private async Task<CommunityCommentResponse> BuildCommentResponseAsync(
        int commentId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var c = await _dbContext.PostComments
            .AsNoTracking()
            .Where(comment => comment.CommentId == commentId)
            .Select(comment => new {
                comment.CommentId,
                comment.PostId,
                comment.UserId,
                Username = comment.User.Username,
                ProfileImageUrl = comment.User.ProfileImageUrl,
                comment.ParentCommentId,
                comment.Content,
                comment.CreatedAt,
                comment.UpdatedAt
            })
            .SingleAsync(cancellationToken);

        var likeCount = await GetCommentLikeCountAsync(c.CommentId, cancellationToken);
        var likedByMe = userId is null ? false : await IsCommentLikedByMeAsync(c.CommentId, userId.Value, cancellationToken);

        return new CommunityCommentResponse(
            c.CommentId,
            c.PostId,
            c.UserId,
            c.Username,
            c.ProfileImageUrl,
            c.ParentCommentId,
            c.Content,
            c.CreatedAt,
            c.UpdatedAt,
            likeCount,
            likedByMe
        );
    }

    [HttpPost("comments/{commentId:int}/like")]
    public async Task<ActionResult> LikeComment(int commentId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var comment = await _dbContext.PostComments.FindAsync(new object[] { commentId }, cancellationToken);
        if (comment is null) return NotFound();

        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            IF NOT EXISTS (SELECT 1 FROM [POST_COMMENT_LIKE] WHERE [commentId] = @commentId AND [userId] = @userId)
            BEGIN
                INSERT INTO [POST_COMMENT_LIKE] ([commentId], [userId]) VALUES (@commentId, @userId);
            END";

        var paramComment = command.CreateParameter();
        paramComment.ParameterName = "@commentId";
        paramComment.Value = commentId;
        command.Parameters.Add(paramComment);

        var paramUser = command.CreateParameter();
        paramUser.ParameterName = "@userId";
        paramUser.Value = userId.Value;
        command.Parameters.Add(paramUser);

        await command.ExecuteNonQueryAsync(cancellationToken);
        return Ok();
    }

    [HttpDelete("comments/{commentId:int}/like")]
    public async Task<ActionResult> UnlikeComment(int commentId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM [POST_COMMENT_LIKE] WHERE [commentId] = @commentId AND [userId] = @userId";

        var paramComment = command.CreateParameter();
        paramComment.ParameterName = "@commentId";
        paramComment.Value = commentId;
        command.Parameters.Add(paramComment);

        var paramUser = command.CreateParameter();
        paramUser.ParameterName = "@userId";
        paramUser.Value = userId.Value;
        command.Parameters.Add(paramUser);

        await command.ExecuteNonQueryAsync(cancellationToken);
        return Ok();
    }

    private async Task<int> GetCommentLikeCountAsync(int commentId, CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM [POST_COMMENT_LIKE] WHERE [commentId] = @commentId";
        var param = command.CreateParameter();
        param.ParameterName = "@commentId";
        param.Value = commentId;
        command.Parameters.Add(param);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private async Task<bool> IsCommentLikedByMeAsync(int commentId, int userId, CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM [POST_COMMENT_LIKE] WHERE [commentId] = @commentId AND [userId] = @userId";
        
        var paramComment = command.CreateParameter();
        paramComment.ParameterName = "@commentId";
        paramComment.Value = commentId;
        command.Parameters.Add(paramComment);
        
        var paramUser = command.CreateParameter();
        paramUser.ParameterName = "@userId";
        paramUser.Value = userId;
        command.Parameters.Add(paramUser);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private async Task<Player> GetOrCreatePlayerAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        var player = await _dbContext.Players
            .Where(player => player.UserId == userId)
            .OrderByDescending(player => player.Prestige)
            .ThenByDescending(player => player.SkillLevel)
            .ThenByDescending(player => player.PlayerId)
            .FirstOrDefaultAsync(cancellationToken);

        if (player is not null)
        {
            return player;
        }

        player = new Player
        {
            UserId = userId,
            Prestige = 0,
            SkillLevel = 0,
            PlayerSubType = "Casual"
        };
        _dbContext.Players.Add(player);

        return player;
    }

    private async Task<GroupMember?> GetMembershipAsync(
        int groupId,
        int userId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.GroupMembers
            .SingleOrDefaultAsync(member => member.GroupId == groupId && member.UserId == userId, cancellationToken);
    }

    private async Task<bool> CanViewGroupAsync(
        int groupId,
        int userId,
        CancellationToken cancellationToken)
    {
        var group = await _dbContext.SocialGroups
            .AsNoTracking()
            .Where(group => group.GroupId == groupId)
            .Select(group => new
            {
                group.GroupType,
                MembershipStatus = group.GroupMembers
                    .Where(member => member.UserId == userId)
                    .Select(member => member.Status)
                    .FirstOrDefault()
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (group is null)
        {
            return false;
        }

        return string.Equals(group.GroupType, PublicGroup, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(group.MembershipStatus, AcceptedStatus, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> CanInteractWithGroupAsync(
        int groupId,
        int userId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.GroupMembers.AnyAsync(
            member =>
                member.GroupId == groupId &&
                member.UserId == userId &&
                member.Status == AcceptedStatus,
            cancellationToken);
    }

    private async Task<bool> CanManagePostAsync(
        Post post,
        int userId,
        CancellationToken cancellationToken)
    {
        if (post.AuthorId == userId)
        {
            return true;
        }

        return post.GroupId is not null &&
               IsGroupManager(await GetMembershipAsync(post.GroupId.Value, userId, cancellationToken));
    }

    private async Task<bool> CanManageCommentAsync(
        PostComment comment,
        int userId,
        CancellationToken cancellationToken)
    {
        if (comment.UserId == userId)
        {
            return true;
        }

        return comment.Post.GroupId is not null &&
               IsGroupManager(await GetMembershipAsync(comment.Post.GroupId.Value, userId, cancellationToken));
    }

    private async Task<Conversation> EnsureGroupConversationAsync(
        int groupId,
        CancellationToken cancellationToken)
    {
        var conversation = await _dbContext.Conversations
            .Where(conversation =>
                conversation.GroupId == groupId &&
                conversation.ConversationType == "Group")
            .OrderBy(conversation => conversation.ConversationId)
            .FirstOrDefaultAsync(cancellationToken);

        if (conversation is null)
        {
            var groupName = await _dbContext.SocialGroups
                .Where(group => group.GroupId == groupId)
                .Select(group => group.GroupName)
                .SingleAsync(cancellationToken);

            conversation = new Conversation
            {
                GroupId = groupId,
                ConversationType = "Group",
                ConversationName = groupName,
                CreatedAt = DateTime.UtcNow
            };
            _dbContext.Conversations.Add(conversation);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var acceptedMemberIds = await _dbContext.GroupMembers
            .Where(member => member.GroupId == groupId && member.Status == AcceptedStatus)
            .Select(member => member.UserId)
            .ToListAsync(cancellationToken);

        var participantIds = await _dbContext.ConversationParticipants
            .Where(participant => participant.ConversationId == conversation.ConversationId)
            .Select(participant => participant.UserId)
            .ToListAsync(cancellationToken);
        var participantSet = participantIds.ToHashSet();

        foreach (var memberId in acceptedMemberIds)
        {
            if (participantSet.Contains(memberId))
            {
                continue;
            }

            _dbContext.ConversationParticipants.Add(new ConversationParticipant
            {
                ConversationId = conversation.ConversationId,
                UserId = memberId,
                JoinedAt = DateTime.UtcNow
            });
        }

        return conversation;
    }

    private async Task EnsureConversationParticipantAsync(
        int conversationId,
        int userId,
        CancellationToken cancellationToken)
    {
        var isAlreadyTracked = _dbContext.ConversationParticipants.Local.Any(
            participant => participant.ConversationId == conversationId && participant.UserId == userId);
        if (isAlreadyTracked)
        {
            return;
        }

        var exists = await _dbContext.ConversationParticipants.AnyAsync(
            participant => participant.ConversationId == conversationId && participant.UserId == userId,
            cancellationToken);
        if (exists)
        {
            return;
        }

        _dbContext.ConversationParticipants.Add(new ConversationParticipant
        {
            ConversationId = conversationId,
            UserId = userId,
            JoinedAt = DateTime.UtcNow
        });
    }

    private async Task NotifyGroupManagersAsync(
        int groupId,
        int requesterUserId,
        string message,
        CancellationToken cancellationToken)
    {
        var managerUserIds = await _dbContext.GroupMembers
            .Where(member =>
                member.GroupId == groupId &&
                member.Status == AcceptedStatus &&
                (member.Role == OwnerRole || member.Role == AdminRole || member.Role == ModeratorRole) &&
                member.UserId != requesterUserId)
            .Select(member => member.UserId)
            .ToListAsync(cancellationToken);

        foreach (var managerUserId in managerUserIds)
        {
            QueueNotification(managerUserId, message);
        }
    }

    private async Task NotifyGroupMembersAsync(
        int groupId,
        int senderUserId,
        string message,
        CancellationToken cancellationToken)
    {
        var memberUserIds = await _dbContext.GroupMembers
            .Where(member =>
                member.GroupId == groupId &&
                member.Status == AcceptedStatus &&
                member.UserId != senderUserId)
            .Select(member => member.UserId)
            .Take(50)
            .ToListAsync(cancellationToken);

        foreach (var memberUserId in memberUserIds)
        {
            QueueNotification(memberUserId, message);
        }
    }

    private void QueueNotification(int userId, string message)
    {
        _notifications.Add(new NotificationInput(
            UserId: userId,
            Type: NotificationTypes.Club,
            Title: "Thông báo cộng đồng",
            Message: message,
            Tone: NotificationTones.Default,
            LinkTo: "/clubs",
            LinkLabel: "Xem CLB"));
    }

    private int? GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out var userId) ? userId : null;
    }

    private static bool IsOwner(GroupMember? member)
    {
        return member is not null &&
               string.Equals(member.Role, OwnerRole, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGroupManager(GroupMember? member)
    {
        return member is not null &&
               string.Equals(member.Status, AcceptedStatus, StringComparison.OrdinalIgnoreCase) &&
               (string.Equals(member.Role, OwnerRole, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(member.Role, AdminRole, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(member.Role, ModeratorRole, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeGroupType(string? groupType)
    {
        return string.Equals(groupType?.Trim(), PrivateGroup, StringComparison.OrdinalIgnoreCase)
            ? PrivateGroup
            : PublicGroup;
    }

    private static string? NormalizeRequired(string? value)
    {
        var normalized = NormalizeOptional(value);
        return normalized?.Length > 0 ? normalized : null;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static IReadOnlyList<string> NormalizeMediaUrls(IReadOnlyList<string>? mediaUrls)
    {
        return mediaUrls?
            .Select(NormalizeOptional)
            .Where(url => url is not null)
            .Select(url => url!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList() ?? [];
    }
}
