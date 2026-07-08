using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Services;

namespace PicklinkBackend.Services;

public partial class CommunityService
{
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

}
