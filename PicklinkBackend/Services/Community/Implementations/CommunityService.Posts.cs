using Microsoft.EntityFrameworkCore;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Services.Community;

namespace PicklinkBackend.Services.Community.Implementations;

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

        var postsQuery = _communityRepository.GroupPosts
            .AsNoTracking()
            .Where(post => post.GroupId == groupId);

        if (!isManager)
        {
            postsQuery = postsQuery.Where(post =>
                post.Visibility == PublicGroup || post.AuthorId == userId.Value);
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
                    .FirstOrDefault(),
                post.Group != null ? post.Group.GroupName : null,
                post.Author.Players
                    .OrderByDescending(player => player.PlayerId)
                    .Select(player => (int?)player.PlayerId)
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
            return BadRequest(new { message = "Vui lòng nhập nội dung hoặc đính kèm ảnh cho bài đăng." });
        }

        var group = await _communityRepository.SocialGroups
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.GroupId == groupId, cancellationToken);
        if (group is null)
        {
            return NotFound();
        }

        var now = DateTime.UtcNow;
        var post = new Post
        {
            GroupId = groupId,
            AuthorId = userId.Value,
            Content = content,
            PostType = "GroupPost",
            Visibility = group.RequirePostApproval ? PendingStatus : PublicGroup,
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

        await _communityRepository.AddPostAsync(post, cancellationToken);
        await _communityRepository.SaveChangesAsync(cancellationToken);

        var response = await BuildPostResponseAsync(post.PostId, userId.Value, cancellationToken);
        return CreatedAtAction(nameof(Posts), new { groupId }, response);
    }

    public async Task<CommunityServiceResult<IReadOnlyList<CommunityPostResponse>>> GetCommunityPosts(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var viewerId = userId ?? 0;
        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);

        var postsQuery = _communityRepository.GroupPosts
            .AsNoTracking()
            .Where(post => post.GroupId == null)
            .Where(post => post.Visibility == PublicGroup ||
                (userId.HasValue &&
                 (post.AuthorId == viewerId ||
                  (post.Visibility == FriendsVisibility && _communityRepository.Friendships.Any(friendship =>
                      friendship.Status == AcceptedStatus &&
                      ((friendship.RequesterId == viewerId && friendship.ReceiverId == post.AuthorId) ||
                       (friendship.ReceiverId == viewerId && friendship.RequesterId == post.AuthorId)))))))
            .OrderByDescending(post => post.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);

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
                    : null,
                post.Group != null ? post.Group.GroupName : null,
                post.Author.Players
                    .OrderByDescending(player => player.PlayerId)
                    .Select(player => (int?)player.PlayerId)
                    .FirstOrDefault()))
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

        var visibility = string.Equals(request.Visibility, FriendsVisibility, StringComparison.OrdinalIgnoreCase)
            ? FriendsVisibility
            : PublicGroup;

        var now = DateTime.UtcNow;
        var post = new Post
        {
            GroupId = null,
            AuthorId = userId.Value,
            Content = content,
            PostType = "Post",
            Visibility = visibility,
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

        await _communityRepository.AddPostAsync(post, cancellationToken);
        await _communityRepository.SaveChangesAsync(cancellationToken);

        var response = await BuildPostResponseAsync(post.PostId, userId.Value, cancellationToken);
        return CreatedAtAction(nameof(GetCommunityPosts), null, response);
    }

    public async Task<CommunityServiceResult<CommunityPostResponse>> GetPost(
        int postId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        var post = await _communityRepository.GroupPosts
            .SingleOrDefaultAsync(p => p.PostId == postId, cancellationToken);

        if (post is null)
        {
            return NotFound();
        }

        if (!await CanViewPostAsync(post, userId, cancellationToken))
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

        var post = await _communityRepository.GroupPosts
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
            return BadRequest(new { message = "Vui lòng nhập nội dung bài đăng." });
        }

        post.Content = content;
        post.UpdatedAt = DateTime.UtcNow;

        await _communityRepository.SaveChangesAsync(cancellationToken);
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

        var post = await _communityRepository.GroupPosts
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

        await _communityRepository.RemovePostAsync(post, cancellationToken);
        await _communityRepository.SaveChangesAsync(cancellationToken);

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

        var post = await _communityRepository.GroupPosts
            .SingleOrDefaultAsync(post => post.PostId == postId, cancellationToken);
        if (post is null)
        {
            return NotFound();
        }

        if (post.GroupId is null)
        {
            return BadRequest(new { message = "Chỉ bài đăng trong nhóm mới cần duyệt." });
        }

        var member = await GetMembershipAsync(post.GroupId.Value, userId.Value, cancellationToken);
        if (!IsGroupManager(member))
        {
            return Forbid();
        }

        post.Visibility = "Public";
        post.UpdatedAt = DateTime.UtcNow;

        await _communityRepository.SaveChangesAsync(cancellationToken);
        _notifications.PublishPending();

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

        var post = await _communityRepository.GroupPosts
            .SingleOrDefaultAsync(post => post.PostId == postId, cancellationToken);
        if (post is null)
        {
            return NotFound();
        }

        if (!await CanViewPostAsync(post, userId.Value, cancellationToken) ||
            (post.GroupId is not null &&
             !await CanInteractWithGroupAsync(post.GroupId.Value, userId.Value, cancellationToken)))
        {
            return Forbid();
        }

        var reactionType = NormalizeOptional(request.ReactionType) ?? "Like";
        var existingLike = await _communityRepository.PostLikes
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
            await _communityRepository.AddLikeAsync(existingLike, cancellationToken);
        }
        else
        {
            existingLike.ReactionType = reactionType;
        }

        if (post.AuthorId != userId.Value)
        {
            QueueNotification(post.AuthorId, "Có người vừa bày tỏ cảm xúc về bài viết của bạn.", $"/posts/{postId}", "Xem bài viết");
        }

        await _communityRepository.SaveChangesAsync(cancellationToken);
        _notifications.PublishPending();

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

        var post = await _communityRepository.GroupPosts
            .SingleOrDefaultAsync(post => post.PostId == postId, cancellationToken);
        if (post is null)
        {
            return NotFound();
        }

        if (!await CanViewPostAsync(post, userId.Value, cancellationToken))
        {
            return Forbid();
        }

        var existingLike = await _communityRepository.PostLikes
            .SingleOrDefaultAsync(like => like.PostId == postId && like.UserId == userId.Value, cancellationToken);
        if (existingLike is not null)
        {
            await _communityRepository.RemoveLikeAsync(existingLike, cancellationToken);
            await _communityRepository.SaveChangesAsync(cancellationToken);
        }

        var response = await BuildPostResponseAsync(postId, userId.Value, cancellationToken);
        return Ok(response);
    }
}
