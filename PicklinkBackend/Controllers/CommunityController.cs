using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.Models;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class CommunityController : ControllerBase
{
    private const string PublicGroup = "Public";
    private const string PrivateGroup = "Private";
    private const string AcceptedStatus = "Accepted";
    private const string PendingStatus = "Pending";
    private const string OwnerRole = "Owner";
    private const string AdminRole = "Admin";
    private const string ModeratorRole = "Moderator";
    private const string MemberRole = "Member";

    private readonly ApplicationDbContext _dbContext;

    public CommunityController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet("groups")]
    public async Task<ActionResult<IReadOnlyList<CommunityGroupResponse>>> Groups(
        [FromQuery] string? query,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var groupsQuery = _dbContext.SocialGroups.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            groupsQuery = groupsQuery.Where(group =>
                EF.Functions.Like(group.GroupName, $"%{term}%") ||
                (group.Description != null && EF.Functions.Like(group.Description, $"%{term}%")));
        }

        var groups = await groupsQuery
            .OrderByDescending(group => group.CreatedAt)
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
                group.GroupMembers
                    .Where(member => member.UserId == userId.Value)
                    .Select(member => member.Role)
                    .FirstOrDefault(),
                group.GroupMembers
                    .Where(member => member.UserId == userId.Value)
                    .Select(member => member.Status)
                    .FirstOrDefault(),
                group.Posts.Count,
                _dbContext.Messages.Count(message =>
                    message.Conversation.GroupId == group.GroupId &&
                    !message.IsDeleted)))
            .ToListAsync(cancellationToken);

        return Ok(groups);
    }

    [HttpGet("groups/{groupId:int}")]
    public async Task<ActionResult<CommunityGroupResponse>> Group(
        int groupId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var group = await BuildGroupResponseAsync(groupId, userId.Value, cancellationToken);
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

        group.Description = NormalizeOptional(request.Description);
        group.GroupType = NormalizeGroupType(request.GroupType);
        group.CoverImageUrl = NormalizeOptional(request.CoverImageUrl);

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

        var posts = await _dbContext.Posts
            .AsNoTracking()
            .Where(post => post.GroupId == groupId)
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
            PostType = "Post",
            Visibility = "Group",
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

        if (post.GroupId is null ||
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

        if (post.GroupId is null ||
            !await CanViewGroupAsync(post.GroupId.Value, userId.Value, cancellationToken))
        {
            return Forbid();
        }

        var comments = await _dbContext.PostComments
            .AsNoTracking()
            .Where(comment => comment.PostId == postId)
            .OrderBy(comment => comment.CreatedAt)
            .Select(comment => new CommunityCommentResponse(
                comment.CommentId,
                comment.PostId,
                comment.UserId,
                comment.User.Username,
                comment.User.ProfileImageUrl,
                comment.ParentCommentId,
                comment.Content,
                comment.CreatedAt,
                comment.UpdatedAt))
            .ToListAsync(cancellationToken);

        return Ok(comments);
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

        if (post.GroupId is null ||
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

    [HttpGet("groups/{groupId:int}/messages")]
    public async Task<ActionResult<IReadOnlyList<CommunityMessageResponse>>> Messages(
        int groupId,
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

        var conversation = await EnsureGroupConversationAsync(groupId, cancellationToken);
        await EnsureConversationParticipantAsync(conversation.ConversationId, userId.Value, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var messages = await _dbContext.Messages
            .AsNoTracking()
            .Where(message => message.ConversationId == conversation.ConversationId && !message.IsDeleted)
            .OrderBy(message => message.SentAt)
            .Take(200)
            .Select(message => new CommunityMessageResponse(
                message.MessageId,
                message.ConversationId,
                message.SenderId,
                message.Sender.Username,
                message.Sender.ProfileImageUrl,
                message.Content,
                message.MessageType,
                message.MediaUrl,
                message.ReplyToMessageId,
                message.SentAt,
                message.SenderId == userId.Value))
            .ToListAsync(cancellationToken);

        return Ok(messages);
    }

    [HttpPost("groups/{groupId:int}/messages")]
    public async Task<ActionResult<CommunityMessageResponse>> SendMessage(
        int groupId,
        SendCommunityMessageRequest request,
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
        var mediaUrl = NormalizeOptional(request.MediaUrl);
        if (content is null && mediaUrl is null)
        {
            return BadRequest(new { message = "Message content or media is required." });
        }

        var conversation = await EnsureGroupConversationAsync(groupId, cancellationToken);
        await EnsureConversationParticipantAsync(conversation.ConversationId, userId.Value, cancellationToken);

        var now = DateTime.UtcNow;
        var message = new Message
        {
            ConversationId = conversation.ConversationId,
            SenderId = userId.Value,
            Content = content,
            MessageType = mediaUrl is null ? "Text" : "Media",
            MediaUrl = mediaUrl,
            ReplyToMessageId = request.ReplyToMessageId,
            SentAt = now,
            IsDeleted = false
        };

        conversation.LastMessageAt = now;
        _dbContext.Messages.Add(message);
        await NotifyGroupMembersAsync(groupId, userId.Value, "New group message.", cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = await _dbContext.Messages
            .AsNoTracking()
            .Where(savedMessage => savedMessage.MessageId == message.MessageId)
            .Select(savedMessage => new CommunityMessageResponse(
                savedMessage.MessageId,
                savedMessage.ConversationId,
                savedMessage.SenderId,
                savedMessage.Sender.Username,
                savedMessage.Sender.ProfileImageUrl,
                savedMessage.Content,
                savedMessage.MessageType,
                savedMessage.MediaUrl,
                savedMessage.ReplyToMessageId,
                savedMessage.SentAt,
                true))
            .SingleAsync(cancellationToken);

        return Ok(response);
    }

    private async Task<CommunityGroupResponse?> BuildGroupResponseAsync(
        int groupId,
        int userId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.SocialGroups
            .AsNoTracking()
            .Where(group => group.GroupId == groupId)
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
                group.GroupMembers
                    .Where(member => member.UserId == userId)
                    .Select(member => member.Role)
                    .FirstOrDefault(),
                group.GroupMembers
                    .Where(member => member.UserId == userId)
                    .Select(member => member.Status)
                    .FirstOrDefault(),
                group.Posts.Count,
                _dbContext.Messages.Count(message =>
                    message.Conversation.GroupId == group.GroupId &&
                    !message.IsDeleted)))
            .SingleOrDefaultAsync(cancellationToken);
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
        return await _dbContext.PostComments
            .AsNoTracking()
            .Where(comment => comment.CommentId == commentId)
            .Select(comment => new CommunityCommentResponse(
                comment.CommentId,
                comment.PostId,
                comment.UserId,
                comment.User.Username,
                comment.User.ProfileImageUrl,
                comment.ParentCommentId,
                comment.Content,
                comment.CreatedAt,
                comment.UpdatedAt))
            .SingleAsync(cancellationToken);
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
        _dbContext.NotificationLogs.Add(new NotificationLog
        {
            UserId = userId,
            Message = message,
            IsRead = false
        });
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

public sealed record CreateCommunityGroupRequest(
    string? GroupName,
    string? Description,
    string? GroupType,
    string? CoverImageUrl);

public sealed record UpdateCommunityGroupRequest(
    string? GroupName,
    string? Description,
    string? GroupType,
    string? CoverImageUrl);

public sealed record CreateCommunityPostRequest(
    string? Content,
    IReadOnlyList<string>? MediaUrls);

public sealed record UpdateCommunityPostRequest(string? Content);

public sealed record ReactToPostRequest(string? ReactionType);

public sealed record CreateCommunityCommentRequest(
    string? Content,
    int? ParentCommentId);

public sealed record UpdateCommunityCommentRequest(string? Content);

public sealed record SendCommunityMessageRequest(
    string? Content,
    string? MediaUrl,
    int? ReplyToMessageId);

public sealed record CommunityGroupResponse(
    int GroupId,
    string GroupName,
    string? Description,
    string GroupType,
    string? CoverImageUrl,
    DateTime CreatedAt,
    int OwnerPlayerId,
    string OwnerName,
    int MemberCount,
    string? MyRole,
    string? MyStatus,
    int PostCount,
    int MessageCount);

public sealed record CommunityMemberResponse(
    int GroupId,
    int UserId,
    string Username,
    string? ProfileImageUrl,
    string Role,
    string Status,
    DateTime JoinedAt);

public sealed record CommunityPostResponse(
    int PostId,
    int? GroupId,
    int AuthorId,
    string AuthorName,
    string? AuthorAvatarUrl,
    string? Content,
    string PostType,
    string Visibility,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<string> MediaUrls,
    int LikeCount,
    int CommentCount,
    bool LikedByMe,
    string? MyReactionType);

public sealed record CommunityCommentResponse(
    int CommentId,
    int PostId,
    int UserId,
    string Username,
    string? UserAvatarUrl,
    int? ParentCommentId,
    string Content,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CommunityMessageResponse(
    int MessageId,
    int ConversationId,
    int SenderId,
    string SenderName,
    string? SenderAvatarUrl,
    string? Content,
    string MessageType,
    string? MediaUrl,
    int? ReplyToMessageId,
    DateTime SentAt,
    bool IsMine);
