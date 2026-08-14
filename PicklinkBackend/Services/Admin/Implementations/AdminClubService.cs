using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Repositories;
using PicklinkBackend.Services.Admin;

namespace PicklinkBackend.Services.Admin.Implementations;

public sealed class AdminClubService : IAdminClubService
{
    private readonly IAdminRepository _adminRepository;

    public AdminClubService(IAdminRepository adminRepository)
    {
        _adminRepository = adminRepository;
    }

    public async Task<PaginatedResponse<AdminClubResponse>> ListAsync(
        string? search,
        bool? suspendedOnly,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);
        var keyword = search?.Trim();

        var (items, totalCount) = await _adminRepository.GetAdminClubListAsync(
            keyword, suspendedOnly, page, pageSize, cancellationToken);

        return Pagination.Create(items, totalCount, page, pageSize);
    }

    public async Task<AdminClubModerationResult> ModerateAsync(
        int groupId,
        AdminClubModerationRequest request,
        int moderatorId,
        string? moderatorName,
        CancellationToken cancellationToken)
    {
        var group = await _adminRepository.GetGroupForModerationByIdAsync(groupId, cancellationToken);
        if (group is null)
            return AdminClubModerationResult.NotFound("Không tìm thấy câu lạc bộ.");

        group.IsSuspended = request.IsSuspended;
        group.SuspensionReason = string.IsNullOrWhiteSpace(request.SuspensionReason)
            ? null
            : request.SuspensionReason.Trim();
        group.ModeratedAt = DateTime.UtcNow;
        group.ModeratedByUserId = moderatorId;

        await _adminRepository.SaveChangesAsync(cancellationToken);

        var response = Map(group);
        response.ModeratedByName = moderatorName;
        return AdminClubModerationResult.Success(response);
    }

    public static AdminClubResponse Map(SocialGroup group) => new()
    {
        GroupId = group.GroupId,
        GroupName = group.GroupName,
        Description = group.Description,
        GroupType = group.GroupType,
        OwnerId = group.OwnerId,
        OwnerName = group.Owner.User.Username,
        MemberCount = group.GroupMembers.Count(member => member.Status == "Accepted"),
        PostCount = group.Posts.Count,
        IsSuspended = group.IsSuspended,
        SuspensionReason = group.SuspensionReason,
        ModeratedAt = group.ModeratedAt,
        ModeratedByName = group.ModeratedByUser?.Username,
        CreatedAt = group.CreatedAt
    };
}
