using System.Data;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;

namespace PicklinkBackend.Services;

public partial class CommunityService
{
    public async Task<CommunityServiceResult<IReadOnlyList<CommunityGroupResponse>>> Groups(
        string? query,
        string? groupType,
        string? sortBy,
        int? page,
        int? pageSize,
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
    public async Task<CommunityServiceResult<CommunityGroupResponse>> Group(
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
    public async Task<CommunityServiceResult<CommunityGroupResponse>> CreateGroup(
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
        return CreatedAtAction(nameof(Group), new { groupId = group.GroupId }, response!);
    }
    public async Task<CommunityServiceResult<CommunityGroupResponse>> UpdateGroup(
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
        return Ok(response!);
    }
    public async Task<CommunityServiceResult<CommunityGroupResponse>> JoinGroup(
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
        return Ok(response!);
    }
    public async Task<CommunityServiceResult<CommunityGroupResponse>> LeaveGroup(
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
        return Ok(response!);
    }

}
