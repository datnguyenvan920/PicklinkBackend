using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;

namespace PicklinkBackend.Services.Admin;

public sealed class AdminUserQueryService
{
    private static readonly string[] Roles = ["User", "Player", "VenueOwner", "Staff", "Admin"];
    private readonly ApplicationDbContext _dbContext;

    public AdminUserQueryService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
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
            return AdminUserListResult.InvalidRole("Vai trÃƒÂ² ngÃ†Â°Ã¡Â»Âi dÃƒÂ¹ng khÃƒÂ´ng hÃ¡Â»Â£p lÃ¡Â»â€¡.");
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
            query = query.Where(user => user.UserType == normalizedRole);

        if (lockedOnly)
            query = query.Where(user => user.IsLocked);

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

        return AdminUserListResult.Success(Pagination.Create(items, totalCount, page, pageSize));
    }

    internal static AdminUserSummaryResponse MapUser(User user) => new()
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

    private static string? NormalizeRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role) || role.Equals("all", StringComparison.OrdinalIgnoreCase))
            return null;

        return Roles.FirstOrDefault(candidate => candidate.Equals(role.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static string RoleLabel(string role) => role switch
    {
        "Admin" => "Admin",
        "Player" => "NgÃ†Â°Ã¡Â»Âi chÃ†Â¡i",
        "VenueOwner" => "ChÃ¡Â»Â§ sÃƒÂ¢n",
        "Staff" => "NhÃƒÂ¢n viÃƒÂªn",
        _ => "ChÃ†Â°a chÃ¡Â»Ân vai trÃƒÂ²"
    };
}

public sealed record AdminUserListResult(
    PaginatedResponse<AdminUserSummaryResponse>? Users,
    string? ErrorMessage)
{
    public bool IsInvalidRole => ErrorMessage is not null;

    public static AdminUserListResult Success(PaginatedResponse<AdminUserSummaryResponse> users) =>
        new(users, ErrorMessage: null);

    public static AdminUserListResult InvalidRole(string errorMessage) =>
        new(Users: null, ErrorMessage: errorMessage);
}
