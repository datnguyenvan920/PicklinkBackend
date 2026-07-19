using System.Data;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;

namespace PicklinkBackend.Services.Community;

public partial class CommunityService
{
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
                UnreadMessageCount = userId.HasValue
                    ? _dbContext.ConversationParticipants
                        .Where(participant =>
                            participant.UserId == userId.Value &&
                            participant.Conversation.GroupId == g.GroupId)
                        .Select(participant => participant.Conversation.Messages.Count(message =>
                            !message.IsDeleted &&
                            message.SenderId != userId.Value &&
                            message.SentAt >= participant.JoinedAt &&
                            (!participant.LastReadAt.HasValue || message.SentAt > participant.LastReadAt.Value)))
                        .FirstOrDefault()
                    : 0,
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
            group.ActiveLocation,
            group.UnreadMessageCount);
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

}
