namespace PicklinkBackend.Services.Admin;

public interface IAdminUserService
{
    Task<AdminUserListResult> ListAsync(
        string? search,
        string? role,
        bool lockedOnly,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<AdminUserLockResult> LockAsync(
        int userId,
        string? reason,
        int? actorId,
        string? actorName,
        CancellationToken cancellationToken);

    Task<AdminUserLockResult> UnlockAsync(
        int userId,
        int? actorId,
        string? actorName,
        CancellationToken cancellationToken);
}
