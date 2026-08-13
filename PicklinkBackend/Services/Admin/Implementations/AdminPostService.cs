using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Repositories;
using PicklinkBackend.Services.Admin;

namespace PicklinkBackend.Services.Admin.Implementations;

public sealed class AdminPostService : IAdminPostService
{
    private readonly IAdminRepository _adminRepository;

    public AdminPostService(IAdminRepository adminRepository)
    {
        _adminRepository = adminRepository;
    }

    public async Task<PaginatedResponse<AdminPostResponse>> ListAsync(
        string? search,
        bool? hiddenOnly,
        int? groupId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);
        var keyword = search?.Trim();

        var (items, totalCount) = await _adminRepository.GetAdminPostListAsync(
            keyword, hiddenOnly, groupId, page, pageSize, cancellationToken);

        return Pagination.Create(items, totalCount, page, pageSize);
    }

    public async Task<AdminPostModerationResult> ModerateAsync(
        int postId,
        AdminPostModerationRequest request,
        int moderatorId,
        string? moderatorName,
        CancellationToken cancellationToken)
    {
        var post = await _adminRepository.GetPostForModerationByIdAsync(postId, cancellationToken);
        if (post is null)
            return AdminPostModerationResult.NotFound("Không tìm thấy bài viết.");

        post.IsHidden = request.IsHidden;
        post.ModerationNote = string.IsNullOrWhiteSpace(request.ModerationNote)
            ? null
            : request.ModerationNote.Trim();
        post.ModeratedAt = DateTime.UtcNow;
        post.ModeratedByUserId = moderatorId;

        await _adminRepository.SaveChangesAsync(cancellationToken);

        var response = Map(post);
        response.ModeratedByName = moderatorName;
        return AdminPostModerationResult.Success(response);
    }

    public async Task<AdminPostDeleteResult> DeleteAsync(
        int postId,
        CancellationToken cancellationToken)
    {
        var post = await _adminRepository.GetPostForModerationByIdAsync(postId, cancellationToken);
        if (post is null)
            return AdminPostDeleteResult.NotFound("Không tìm thấy bài viết.");

        await _adminRepository.RemovePostAsync(post, cancellationToken);
        await _adminRepository.SaveChangesAsync(cancellationToken);

        return AdminPostDeleteResult.Success();
    }

    public static AdminPostResponse Map(Post post) => new()
    {
        PostId = post.PostId,
        AuthorId = post.AuthorId,
        AuthorName = post.Author.Username,
        AuthorEmail = post.Author.Email,
        GroupId = post.GroupId,
        GroupName = post.Group?.GroupName,
        Content = post.Content,
        PostType = post.PostType,
        Visibility = post.Visibility,
        IsHidden = post.IsHidden,
        ModerationNote = post.ModerationNote,
        ModeratedAt = post.ModeratedAt,
        ModeratedByName = post.ModeratedByUser?.Username,
        LikeCount = post.PostLikes.Count,
        CommentCount = post.PostComments.Count,
        CreatedAt = post.CreatedAt
    };
}
