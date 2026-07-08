using System.Data;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;

namespace PicklinkBackend.Services;

public partial class CommunityService
{
    public async Task<CommunityServiceResult<IReadOnlyList<CommunityPostResponse>>> Posts(
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
    public async Task<CommunityServiceResult<CommunityPostResponse>> CreatePost(
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
    public async Task<CommunityServiceResult<IReadOnlyList<CommunityPostResponse>>> GetCommunityPosts(
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
    public async Task<CommunityServiceResult<CommunityPostResponse>> CreateCommunityPost(
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
    public async Task<CommunityServiceResult<CommunityPostResponse>> GetPost(
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
    public async Task<CommunityServiceResult<CommunityPostResponse>> UpdatePost(
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
    public async Task<CommunityServiceResult> DeletePost(
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
    public async Task<CommunityServiceResult<CommunityPostResponse>> ApprovePost(
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
    public async Task<CommunityServiceResult<CommunityPostResponse>> ReactToPost(
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
    public async Task<CommunityServiceResult<CommunityPostResponse>> RemoveReaction(
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

}
