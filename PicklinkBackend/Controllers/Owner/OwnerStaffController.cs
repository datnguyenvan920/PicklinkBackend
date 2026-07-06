using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Services;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize(Roles = "VenueOwner")]
[Route("api/owner/staff")]
public class OwnerStaffController : ControllerBase
{
    public static readonly string[] AllowedPermissions =
    {
        "ViewBookings", "VerifyBooking", "ConfirmPayment", "CheckIn", "MarkNoShow"
    };

    private readonly ApplicationDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;

    public OwnerStaffController(ApplicationDbContext dbContext, IPasswordHasher passwordHasher)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
    }

    [HttpGet]
    public async Task<ActionResult<List<OwnerStaffResponse>>> GetStaff(CancellationToken cancellationToken)
    {
        var ownerId = CurrentUserId();
        if (ownerId is null) return Unauthorized();

        var assignments = await _dbContext.Staff.AsNoTracking()
            .Where(item => item.Venue.Owner.UserId == ownerId)
            .Include(item => item.User)
            .Include(item => item.Venue)
            .OrderByDescending(item => item.IsActive)
            .ThenBy(item => item.User.Username)
            .ThenBy(item => item.Venue.VenueName)
            .ToListAsync(cancellationToken);

        return Ok(assignments.Select(MapStaff).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<OwnerStaffResponse>> AssignStaff(
        AssignStaffRequest request,
        CancellationToken cancellationToken)
    {
        var ownerUserId = CurrentUserId();
        if (ownerUserId is null) return Unauthorized();
        var venue = await _dbContext.Venues
            .SingleOrDefaultAsync(item => item.VenueId == request.VenueId && item.Owner.UserId == ownerUserId, cancellationToken);
        if (venue is null) return NotFound(new { message = "Không tìm thấy cụm sân thuộc tài khoản Owner." });

        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _dbContext.Users.SingleOrDefaultAsync(item => item.Email.ToLower() == email, cancellationToken);
        if (user is null) return NotFound(new { message = "Không tìm thấy tài khoản với email này. Staff cần đăng ký tài khoản trước." });
        if (user.UserId == ownerUserId) return Conflict(new { message = "Owner không thể tự gán mình làm Staff." });
        if (user.UserType is "Admin" or "VenueOwner") return Conflict(new { message = "Không thể gán tài khoản Admin hoặc Owner làm Staff." });

        var permissions = NormalizePermissions(request.Permissions);
        if (permissions.Count == 0) return BadRequest(new { message = "Hãy cấp ít nhất một quyền cho Staff." });

        var assignment = await _dbContext.Staff
            .Include(item => item.User)
            .Include(item => item.Venue)
            .SingleOrDefaultAsync(item => item.UserId == user.UserId && item.VenueId == venue.VenueId, cancellationToken);
        if (assignment is null)
        {
            assignment = new Staff
            {
                UserId = user.UserId,
                VenueId = venue.VenueId,
                Role = CleanRole(request.Role),
                Permissions = string.Join(',', permissions),
                IsActive = true,
                AssignedAt = DateTime.UtcNow,
                AssignedByUserId = ownerUserId
            };
            _dbContext.Staff.Add(assignment);
        }
        else
        {
            assignment.Role = CleanRole(request.Role);
            assignment.Permissions = string.Join(',', permissions);
            assignment.IsActive = true;
            assignment.AssignedAt = DateTime.UtcNow;
            assignment.AssignedByUserId = ownerUserId;
            assignment.RevokedAt = null;
        }

        user.UserType = "Staff";
        _dbContext.VenueAuditLogs.Add(NewAudit(venue.VenueId, ownerUserId.Value, $"StaffAssigned:{user.UserId}"));
        await _dbContext.SaveChangesAsync(cancellationToken);
        assignment.User = user;
        assignment.Venue = venue;
        return Ok(MapStaff(assignment));
    }

    [HttpPost("accounts")]
    public async Task<ActionResult<OwnerStaffResponse>> CreateStaffAccount(
        CreateStaffAccountRequest request,
        CancellationToken cancellationToken)
    {
        var ownerUserId = CurrentUserId();
        if (ownerUserId is null) return Unauthorized();
        var venue = await _dbContext.Venues
            .SingleOrDefaultAsync(item => item.VenueId == request.VenueId && item.Owner.UserId == ownerUserId, cancellationToken);
        if (venue is null) return NotFound(new { message = "Không tìm thấy cụm sân thuộc tài khoản Owner." });

        var username = request.Username.Trim();
        var email = request.Email.Trim().ToLowerInvariant();
        if (await _dbContext.Users.AnyAsync(item => item.Email == email, cancellationToken))
            return Conflict(new { message = "Email đã tồn tại. Hãy chọn 'Gán tài khoản có sẵn' để phân công tài khoản này." });
        if (await _dbContext.Users.AnyAsync(item => item.Username == username, cancellationToken))
            return Conflict(new { message = "Tên đăng nhập đã được sử dụng." });

        var permissions = NormalizePermissions(request.Permissions);
        if (permissions.Count == 0) return BadRequest(new { message = "Hãy cấp ít nhất một quyền cho Staff." });

        var user = new User
        {
            Username = username,
            Email = email,
            PasswordHash = _passwordHasher.Hash(request.Password),
            UserType = "Staff"
        };
        var assignment = new Staff
        {
            User = user,
            Venue = venue,
            Role = CleanRole(request.Role),
            Permissions = string.Join(',', permissions),
            IsActive = true,
            AssignedAt = DateTime.UtcNow,
            AssignedByUserId = ownerUserId
        };
        _dbContext.Users.Add(user);
        _dbContext.Staff.Add(assignment);
        _dbContext.VenueAuditLogs.Add(NewAudit(venue.VenueId, ownerUserId.Value, $"StaffAccountCreated:{email}"));
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(MapStaff(assignment));
    }

    [HttpPatch("{staffId:int}")]
    public async Task<ActionResult<OwnerStaffResponse>> UpdateStaff(
        int staffId,
        UpdateStaffRequest request,
        CancellationToken cancellationToken)
    {
        var ownerUserId = CurrentUserId();
        if (ownerUserId is null) return Unauthorized();
        var assignment = await _dbContext.Staff
            .Include(item => item.User)
            .Include(item => item.Venue)
            .SingleOrDefaultAsync(item => item.StaffId == staffId && item.Venue.Owner.UserId == ownerUserId, cancellationToken);
        if (assignment is null) return NotFound(new { message = "Không tìm thấy phân công Staff." });

        var permissions = NormalizePermissions(request.Permissions);
        if (request.IsActive && permissions.Count == 0)
            return BadRequest(new { message = "Staff đang hoạt động cần có ít nhất một quyền." });

        assignment.Role = CleanRole(request.Role);
        assignment.Permissions = string.Join(',', permissions);
        assignment.IsActive = request.IsActive;
        assignment.RevokedAt = request.IsActive ? null : DateTime.UtcNow;
        _dbContext.VenueAuditLogs.Add(NewAudit(
            assignment.VenueId,
            ownerUserId.Value,
            request.IsActive ? $"StaffPermissionsUpdated:{assignment.UserId}" : $"StaffRevoked:{assignment.UserId}"));
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(MapStaff(assignment));
    }

    [HttpGet("check-in-history")]
    public async Task<ActionResult<PaginatedResponse<OwnerCheckInHistoryResponse>>> GetCheckInHistory(
        int? venueId,
        DateOnly? date,
        int page = 1,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var ownerUserId = CurrentUserId();
        if (ownerUserId is null) return Unauthorized();
        var query = _dbContext.BookingOperations.AsNoTracking()
            .Where(item => item.Booking.Court.Venue.Owner.UserId == ownerUserId);
        if (venueId.HasValue) query = query.Where(item => item.Booking.Court.VenueId == venueId.Value);
        if (date.HasValue)
        {
            var start = date.Value.ToDateTime(TimeOnly.MinValue);
            var end = start.AddDays(1);
            query = query.Where(item => item.Booking.StartTime >= start && item.Booking.StartTime < end);
        }

        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);
        var totalCount = await query.CountAsync(cancellationToken);
        var operations = await query
            .Include(item => item.Booking).ThenInclude(item => item.Court).ThenInclude(item => item.Venue)
            .Include(item => item.Booking).ThenInclude(item => item.Player).ThenInclude(item => item!.User)
            .OrderByDescending(item => item.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var actorIds = operations.SelectMany(item => new[]
            {
                item.CodeVerifiedByUserId, item.PaymentConfirmedByUserId, item.CheckedInByUserId, item.NoShowByUserId
            })
            .Where(item => item.HasValue).Select(item => item!.Value).Distinct().ToList();
        var actors = await _dbContext.Users.AsNoTracking()
            .Where(item => actorIds.Contains(item.UserId))
            .ToDictionaryAsync(item => item.UserId, item => item.Username, cancellationToken);

        return Ok(Pagination.Create(operations.Select(item => MapHistory(item, actors)), totalCount, page, pageSize));
    }

    private int? CurrentUserId() => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private static string CleanRole(string? role) => string.IsNullOrWhiteSpace(role) ? "Nhân viên vận hành" : role.Trim()[..Math.Min(role.Trim().Length, 100)];
    private static List<string> NormalizePermissions(IEnumerable<string>? values)
    {
        var permissions = (values ?? Array.Empty<string>()).Where(value => AllowedPermissions.Contains(value, StringComparer.OrdinalIgnoreCase))
            .Select(value => AllowedPermissions.First(item => item.Equals(value, StringComparison.OrdinalIgnoreCase)))
            .Distinct().ToList();
        if (permissions.Count > 0 && !permissions.Contains("ViewBookings")) permissions.Insert(0, "ViewBookings");
        return permissions;
    }
    private static string[] SplitPermissions(string value) => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private static VenueAuditLog NewAudit(int venueId, int actorId, string action) => new()
    {
        VenueId = venueId, ActorId = actorId, Action = action, Timestamp = DateTime.UtcNow
    };
    private static OwnerStaffResponse MapStaff(Staff item) => new()
    {
        StaffId = item.StaffId, UserId = item.UserId, Username = item.User.Username, Email = item.User.Email,
        VenueId = item.VenueId, VenueName = item.Venue.VenueName, Role = item.Role,
        Permissions = SplitPermissions(item.Permissions), IsActive = item.IsActive,
        AssignedAt = AsUtc(item.AssignedAt), RevokedAt = AsUtc(item.RevokedAt)
    };
    private static OwnerCheckInHistoryResponse MapHistory(BookingOperation item, IReadOnlyDictionary<int, string> actors) => new()
    {
        BookingId = item.BookingId,
        BookingCode = item.Booking.BookingCode ?? $"PL-{item.BookingId}",
        VenueId = item.Booking.Court.VenueId,
        VenueName = item.Booking.Court.Venue.VenueName,
        CourtNumber = item.Booking.Court.CourtNumber,
        PlayerName = item.Booking.Player?.User.Username ?? "Khách",
        StartTime = item.Booking.StartTime,
        CheckInStatus = item.CheckInStatus,
        CodeVerifiedAt = AsUtc(item.CodeVerifiedAt),
        CodeVerifiedBy = ActorName(item.CodeVerifiedByUserId, actors),
        PaymentConfirmedAt = AsUtc(item.PaymentConfirmedAt),
        PaymentConfirmedBy = ActorName(item.PaymentConfirmedByUserId, actors),
        CheckedInAt = AsUtc(item.CheckedInAt),
        CheckedInBy = ActorName(item.CheckedInByUserId, actors),
        NoShowAt = AsUtc(item.NoShowAt),
        NoShowBy = ActorName(item.NoShowByUserId, actors)
    };
    private static string? ActorName(int? id, IReadOnlyDictionary<int, string> actors) => id.HasValue && actors.TryGetValue(id.Value, out var name) ? name : null;
    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static DateTime? AsUtc(DateTime? value) => value.HasValue ? AsUtc(value.Value) : null;
}

public record AssignStaffRequest(int VenueId, string Email, string? Role, List<string>? Permissions);
public record UpdateStaffRequest(string? Role, List<string>? Permissions, bool IsActive);
public class CreateStaffAccountRequest
{
    [System.ComponentModel.DataAnnotations.Range(1, int.MaxValue)]
    public int VenueId { get; set; }

    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.StringLength(100, MinimumLength = 3)]
    public string Username { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.EmailAddress]
    [System.ComponentModel.DataAnnotations.StringLength(255)]
    public string Email { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.StringLength(100, MinimumLength = 8)]
    [System.ComponentModel.DataAnnotations.RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^a-zA-Z\d]).+$", ErrorMessage = "Mật khẩu phải có chữ hoa, chữ thường, số và ký tự đặc biệt.")]
    public string Password { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.StringLength(100)]
    public string? Role { get; set; }

    public List<string>? Permissions { get; set; }
}
public class OwnerStaffResponse
{
    public int StaffId { get; set; }
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int VenueId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string[] Permissions { get; set; } = Array.Empty<string>();
    public bool IsActive { get; set; }
    public DateTime AssignedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}
public class OwnerCheckInHistoryResponse
{
    public int BookingId { get; set; }
    public string BookingCode { get; set; } = string.Empty;
    public int VenueId { get; set; }
    public string VenueName { get; set; } = string.Empty;
    public int CourtNumber { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public string CheckInStatus { get; set; } = string.Empty;
    public DateTime? CodeVerifiedAt { get; set; }
    public string? CodeVerifiedBy { get; set; }
    public DateTime? PaymentConfirmedAt { get; set; }
    public string? PaymentConfirmedBy { get; set; }
    public DateTime? CheckedInAt { get; set; }
    public string? CheckedInBy { get; set; }
    public DateTime? NoShowAt { get; set; }
    public string? NoShowBy { get; set; }
}
