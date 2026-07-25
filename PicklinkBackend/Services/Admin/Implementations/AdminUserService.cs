using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Repositories;
using PicklinkBackend.Services.Admin;

namespace PicklinkBackend.Services.Admin.Implementations;

public sealed class AdminUserService : IAdminUserService
{
    private static readonly string[] Roles = ["User", "Player", "VenueOwner", "Staff", "Admin"];
    private readonly IAdminRepository _adminRepository;

    public AdminUserService(IAdminRepository adminRepository)
    {
        _adminRepository = adminRepository;
    }

    public async Task<AdminUserListResult> ListAsync(
        string? search,
        string? role,
        bool lockedOnly,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);
        var keyword = search?.Trim();
        var normalizedRole = NormalizeRole(role);
        if (!string.IsNullOrWhiteSpace(role)
            && !role.Equals("all", StringComparison.OrdinalIgnoreCase)
            && normalizedRole is null)
        {
            return AdminUserListResult.InvalidRole("Vai trò người dùng không hợp lệ.");
        }

        var (items, totalCount) = await _adminRepository.GetAdminUserListAsync(
            keyword, normalizedRole, lockedOnly, page, pageSize, cancellationToken);

        return AdminUserListResult.Success(Pagination.Create(items.Cast<AdminUserResponse>().ToList(), totalCount, page, pageSize));
    }

    public async Task<AdminUserLockResult> LockAsync(
        int userId,
        int? actorId,
        CancellationToken cancellationToken)
    {
        if (actorId is null) return AdminUserLockResult.Unauthorized();
        if (actorId.Value == userId)
            return AdminUserLockResult.BadRequest("Admin không thể tự khóa tài khoản của mình.");

        var user = await _adminRepository.GetUserForLockByIdAsync(userId, cancellationToken);
        if (user is null)
            return AdminUserLockResult.NotFound("Không tìm thấy người dùng.");

        user.IsLocked = true;
        await _adminRepository.SaveChangesAsync(cancellationToken);

        return AdminUserLockResult.Success(MapUser(user));
    }

    public async Task<AdminUserLockResult> UnlockAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        var user = await _adminRepository.GetUserForLockByIdAsync(userId, cancellationToken);
        if (user is null)
            return AdminUserLockResult.NotFound("Không tìm thấy người dùng.");

        user.IsLocked = false;
        await _adminRepository.SaveChangesAsync(cancellationToken);

        return AdminUserLockResult.Success(MapUser(user));
    }

    public static AdminUserResponse MapUser(User user)
    {
        return new AdminUserResponse
        {
            UserId = user.UserId,
            Name = user.Username,
            Email = user.Email,
            Role = user.UserType,
            RoleLabel = RoleLabel(user.UserType),
            IsLocked = user.IsLocked,
            City = user.City,
            Commune = user.Commune,
            AvatarUrl = user.ProfileImageUrl,
            JoinedClubCount = user.GroupMembers.Count(member => member.Status == "Accepted"),
            OwnedVenueCount = user.VenueOwners.SelectMany(owner => owner.Venues).Count(),
            BookingCount = user.Players.SelectMany(player => player.Bookings).Count()
        };
    }

    private static string RoleLabel(string role) => role switch
    {
        "Admin" => "Quản trị viên",
        "VenueOwner" => "Chủ sân",
        "Staff" => "Nhân viên",
        "Player" => "Người chơi",
        _ => "Người dùng"
    };

    private static string? NormalizeRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role) || role.Equals("all", StringComparison.OrdinalIgnoreCase))
            return null;

        return Roles.FirstOrDefault(item => item.Equals(role.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
