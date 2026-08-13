using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Repositories;
using PicklinkBackend.Services.Admin;
using PicklinkBackend.Services.Auth;

namespace PicklinkBackend.Services.Admin.Implementations;

public sealed class AdminUserService : IAdminUserService
{
    private static readonly string[] Roles = ["User", "Player", "VenueOwner", "Staff", "Admin"];
    private readonly IAdminRepository _adminRepository;
    private readonly AccountStatusCache _accountStatus;

    public AdminUserService(IAdminRepository adminRepository, AccountStatusCache accountStatus)
    {
        _adminRepository = adminRepository;
        _accountStatus = accountStatus;
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
        string? reason,
        int? actorId,
        string? actorName,
        CancellationToken cancellationToken)
    {
        if (actorId is null) return AdminUserLockResult.Unauthorized();
        if (actorId.Value == userId)
            return AdminUserLockResult.BadRequest("Admin không thể tự khóa tài khoản của mình.");

        var user = await _adminRepository.GetUserForLockByIdAsync(userId, cancellationToken);
        if (user is null)
            return AdminUserLockResult.NotFound("Không tìm thấy người dùng.");

        user.IsLocked = true;
        user.LockReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        user.LockedAt = DateTime.UtcNow;
        user.LockedByUserId = actorId.Value;
        await _adminRepository.SaveChangesAsync(cancellationToken);
        _accountStatus.Invalidate(userId);

        var response = MapUser(user);
        response.LockedByName = actorName;
        return AdminUserLockResult.Success(response);
    }

    public async Task<AdminUserLockResult> UnlockAsync(
        int userId,
        int? actorId,
        string? actorName,
        CancellationToken cancellationToken)
    {
        if (actorId is null) return AdminUserLockResult.Unauthorized();

        var user = await _adminRepository.GetUserForLockByIdAsync(userId, cancellationToken);
        if (user is null)
            return AdminUserLockResult.NotFound("Không tìm thấy người dùng.");

        user.IsLocked = false;
        user.LockReason = null;
        user.UnlockedAt = DateTime.UtcNow;
        user.UnlockedByUserId = actorId.Value;
        await _adminRepository.SaveChangesAsync(cancellationToken);
        _accountStatus.Invalidate(userId);

        var response = MapUser(user);
        response.UnlockedByName = actorName;
        return AdminUserLockResult.Success(response);
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
            LockReason = user.LockReason,
            LockedAt = user.LockedAt,
            LockedByName = user.LockedByUser?.Username,
            UnlockedAt = user.UnlockedAt,
            UnlockedByName = user.UnlockedByUser?.Username,
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
