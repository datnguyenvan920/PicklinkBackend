using Microsoft.EntityFrameworkCore;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Services.Community;

namespace PicklinkBackend.Services.Community.Implementations;

public partial class CommunityService
{
    public async Task<CommunityServiceResult<IReadOnlyList<CommunityCommentResponse>>> Comments(
        int postId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var post = await _communityRepository.GroupPosts
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

        var comments = await _communityRepository.GroupComments
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

    public async Task<CommunityServiceResult<CommunityCommentResponse>> CreateComment(
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

        var post = await _communityRepository.GroupPosts
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
            var parentExists = await _communityRepository.GroupComments
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

        await _communityRepository.AddCommentAsync(comment, cancellationToken);
        if (post.AuthorId != userId.Value)
        {
            QueueNotification(post.AuthorId, "Someone commented on your post.");
        }

        await _communityRepository.SaveChangesAsync(cancellationToken);
        _notifications.PublishPending();

        var response = await BuildCommentResponseAsync(comment.CommentId, cancellationToken);
        return CreatedAtAction(nameof(Comments), new { postId }, response);
    }

    public async Task<CommunityServiceResult<CommunityCommentResponse>> UpdateComment(
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

        var comment = await _communityRepository.GroupComments
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

        await _communityRepository.SaveChangesAsync(cancellationToken);

        var response = await BuildCommentResponseAsync(commentId, cancellationToken);
        return Ok(response);
    }

    public async Task<CommunityServiceResult> DeleteComment(
        int commentId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var comment = await _communityRepository.GroupComments
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

        await _communityRepository.RemoveCommentAsync(comment, cancellationToken);
        await _communityRepository.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    public async Task<CommunityServiceResult> LikeComment(int commentId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var comment = await _communityRepository.GroupComments.SingleOrDefaultAsync(c => c.CommentId == commentId, cancellationToken);
        if (comment is null) return NotFound();

        await _communityRepository.LikeCommentAsync(commentId, userId.Value, cancellationToken);
        return Ok();
    }

    public async Task<CommunityServiceResult> UnlikeComment(int commentId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await _communityRepository.UnlikeCommentAsync(commentId, userId.Value, cancellationToken);
        return Ok();
    }

    private Task<int> GetCommentLikeCountAsync(int commentId, CancellationToken cancellationToken)
    {
        return _communityRepository.GetCommentLikeCountAsync(commentId, cancellationToken);
    }

    private Task<bool> IsCommentLikedByMeAsync(int commentId, int userId, CancellationToken cancellationToken)
    {
        return _communityRepository.IsCommentLikedByMeAsync(commentId, userId, cancellationToken);
    }
}
