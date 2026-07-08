using System.Data;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;

namespace PicklinkBackend.Services;

public partial class CommunityService
{
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
        return _currentUserId;
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
