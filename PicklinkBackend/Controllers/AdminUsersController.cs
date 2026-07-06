using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/users")]
public class AdminUsersController : ControllerBase
{
    private static readonly string[] Roles = ["User", "Player", "VenueOwner", "Staff", "Admin"];
    private readonly ApplicationDbContext _dbContext;

    public AdminUsersController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<PaginatedResponse<AdminUserSummaryResponse>>> GetUsers(
        string? search,
        string? role,
        bool lockedOnly = false,
        int page = Pagination.DefaultPage,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);
        var keyword = search?.Trim();
        var normalizedRole = NormalizeRole(role);
        if (!string.IsNullOrWhiteSpace(role)
            && !role.Equals("all", StringComparison.OrdinalIgnoreCase)
            && normalizedRole is null)
        {
            return BadRequest(new { message = "Vai trò người dùng không hợp lệ." });
        }

        var query = _dbContext.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(user =>
                user.Username.Contains(keyword)
                || user.Email.Contains(keyword)
                || (user.City != null && user.City.Contains(keyword))
                || (user.Commune != null && user.Commune.Contains(keyword)));
        }

        if (normalizedRole is not null)
        {
            query = query.Where(user => user.UserType == normalizedRole);
        }

        if (lockedOnly)
        {
            query = query.Where(user => user.IsLocked);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(user => user.IsLocked)
            .ThenBy(user => user.Username)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(user => new AdminUserSummaryResponse
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
            })
            .ToListAsync(cancellationToken);

        return Ok(Pagination.Create(items, totalCount, page, pageSize));
    }

    [HttpPost("{userId:int}/lock")]
    public async Task<ActionResult<AdminUserSummaryResponse>> LockUser(
        int userId,
        AdminUserLockRequest request,
        CancellationToken cancellationToken)
    {
        var actorId = CurrentUserId();
        if (actorId is null) return Unauthorized();
        if (actorId.Value == userId) return BadRequest(new { message = "Admin không thể tự khóa tài khoản của mình." });

        var user = await LoadUser(userId, cancellationToken);
        if (user is null) return NotFound(new { message = "Không tìm thấy người dùng." });

        user.IsLocked = true;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(MapUser(user));
    }

    [HttpPost("{userId:int}/unlock")]
    public async Task<ActionResult<AdminUserSummaryResponse>> UnlockUser(
        int userId,
        CancellationToken cancellationToken)
    {
        var user = await LoadUser(userId, cancellationToken);
        if (user is null) return NotFound(new { message = "Không tìm thấy người dùng." });

        user.IsLocked = false;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(MapUser(user));
    }

    private int? CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : null;

    private async Task<User?> LoadUser(int userId, CancellationToken cancellationToken) =>
        await _dbContext.Users
            .Include(user => user.GroupMembers)
            .Include(user => user.VenueOwners).ThenInclude(owner => owner.Venues)
            .Include(user => user.Players).ThenInclude(player => player.Bookings)
            .SingleOrDefaultAsync(user => user.UserId == userId, cancellationToken);

    private static string? NormalizeRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role) || role.Equals("all", StringComparison.OrdinalIgnoreCase))
            return null;

        return Roles.FirstOrDefault(candidate => candidate.Equals(role.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static AdminUserSummaryResponse MapUser(User user) => new()
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
        OwnedVenueCount = user.VenueOwners.Sum(owner => owner.Venues.Count),
        BookingCount = user.Players.Sum(player => player.Bookings.Count)
    };

    private static string RoleLabel(string role) => role switch
    {
        "Admin" => "Admin",
        "Player" => "Người chơi",
        "VenueOwner" => "Chủ sân",
        "Staff" => "Nhân viên",
        _ => "Chưa chọn vai trò"
    };
}

public class AdminUserLockRequest
{
    public string? Reason { get; set; }
}

public class AdminUserSummaryResponse
{
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string RoleLabel { get; set; } = string.Empty;
    public bool IsLocked { get; set; }
    public string? City { get; set; }
    public string? Commune { get; set; }
    public string? AvatarUrl { get; set; }
    public int JoinedClubCount { get; set; }
    public int OwnedVenueCount { get; set; }
    public int BookingCount { get; set; }
}
