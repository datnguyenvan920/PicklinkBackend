using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;

namespace PicklinkBackend.Services.Admin;

public sealed class AdminUserLockService
{
    private readonly ApplicationDbContext _dbContext;

    public AdminUserLockService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<AdminUserLockResult> LockAsync(
        int userId,
        int? actorId,
        CancellationToken cancellationToken)
    {
        if (actorId is null) return AdminUserLockResult.Unauthorized();
        if (actorId.Value == userId)
            return AdminUserLockResult.BadRequest("Admin khÃƒÂ´ng thÃ¡Â»Æ’ tÃ¡Â»Â± khÃƒÂ³a tÃƒÂ i khoÃ¡ÂºÂ£n cÃ¡Â»Â§a mÃƒÂ¬nh.");

        var user = await LoadUser(userId, cancellationToken);
        if (user is null)
            return AdminUserLockResult.NotFound("KhÃƒÂ´ng tÃƒÂ¬m thÃ¡ÂºÂ¥y ngÃ†Â°Ã¡Â»Âi dÃƒÂ¹ng.");

        user.IsLocked = true;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return AdminUserLockResult.Success(AdminUserQueryService.MapUser(user));
    }

    public async Task<AdminUserLockResult> UnlockAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        var user = await LoadUser(userId, cancellationToken);
        if (user is null)
            return AdminUserLockResult.NotFound("KhÃƒÂ´ng tÃƒÂ¬m thÃ¡ÂºÂ¥y ngÃ†Â°Ã¡Â»Âi dÃƒÂ¹ng.");

        user.IsLocked = false;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return AdminUserLockResult.Success(AdminUserQueryService.MapUser(user));
    }

    private async Task<User?> LoadUser(int userId, CancellationToken cancellationToken) =>
        await _dbContext.Users
            .Include(user => user.GroupMembers)
            .Include(user => user.VenueOwners).ThenInclude(owner => owner.Venues)
            .Include(user => user.Players).ThenInclude(player => player.Bookings)
            .SingleOrDefaultAsync(user => user.UserId == userId, cancellationToken);
}

public sealed record AdminUserLockResult(
    AdminUserLockResultStatus Status,
    AdminUserSummaryResponse? User = null,
    string? ErrorMessage = null)
{
    public static AdminUserLockResult Success(AdminUserSummaryResponse user) =>
        new(AdminUserLockResultStatus.Success, user, ErrorMessage: null);

    public static AdminUserLockResult Unauthorized() =>
        new(AdminUserLockResultStatus.Unauthorized);

    public static AdminUserLockResult BadRequest(string errorMessage) =>
        new(AdminUserLockResultStatus.BadRequest, User: null, ErrorMessage: errorMessage);

    public static AdminUserLockResult NotFound(string errorMessage) =>
        new(AdminUserLockResultStatus.NotFound, User: null, ErrorMessage: errorMessage);
}

public enum AdminUserLockResultStatus
{
    Success,
    Unauthorized,
    BadRequest,
    NotFound
}
