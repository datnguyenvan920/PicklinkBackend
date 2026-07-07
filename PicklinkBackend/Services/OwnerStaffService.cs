using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;

namespace PicklinkBackend.Services;

public sealed class OwnerStaffService
{
    public static readonly string[] AllowedPermissions =
    {
        "ViewBookings", "VerifyBooking", "ConfirmPayment", "CheckIn", "MarkNoShow"
    };

    private readonly ApplicationDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;

    public OwnerStaffService(ApplicationDbContext dbContext, IPasswordHasher passwordHasher)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
    }

    public async Task<OwnerStaffListResult> ListAsync(int? ownerUserId, CancellationToken cancellationToken)
    {
        if (ownerUserId is null) return OwnerStaffListResult.Unauthorized();

        var assignments = await _dbContext.Staff.AsNoTracking()
            .Where(item => item.Venue.Owner.UserId == ownerUserId.Value)
            .Include(item => item.User)
            .Include(item => item.Venue)
            .OrderByDescending(item => item.IsActive)
            .ThenBy(item => item.User.Username)
            .ThenBy(item => item.Venue.VenueName)
            .ToListAsync(cancellationToken);

        return OwnerStaffListResult.Success(assignments.Select(MapStaff).ToList());
    }

    public async Task<OwnerStaffMutationResult> AssignAsync(
        AssignStaffRequest request,
        int? ownerUserId,
        CancellationToken cancellationToken)
    {
        if (ownerUserId is null) return OwnerStaffMutationResult.Unauthorized();

        var venue = await _dbContext.Venues
            .SingleOrDefaultAsync(item => item.VenueId == request.VenueId && item.Owner.UserId == ownerUserId.Value, cancellationToken);
        if (venue is null) return OwnerStaffMutationResult.NotFound("Khong tim thay cum san thuoc tai khoan Owner.");

        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _dbContext.Users.SingleOrDefaultAsync(item => item.Email.ToLower() == email, cancellationToken);
        if (user is null) return OwnerStaffMutationResult.NotFound("Khong tim thay tai khoan voi email nay.");
        if (user.UserId == ownerUserId.Value) return OwnerStaffMutationResult.Conflict("Owner khong the tu gan minh lam Staff.");
        if (user.UserType is "Admin" or "VenueOwner") return OwnerStaffMutationResult.Conflict("Khong the gan tai khoan Admin hoac Owner lam Staff.");

        var permissions = NormalizePermissions(request.Permissions);
        if (permissions.Count == 0) return OwnerStaffMutationResult.BadRequest("Hay cap it nhat mot quyen cho Staff.");

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
                AssignedByUserId = ownerUserId.Value
            };
            _dbContext.Staff.Add(assignment);
        }
        else
        {
            assignment.Role = CleanRole(request.Role);
            assignment.Permissions = string.Join(',', permissions);
            assignment.IsActive = true;
            assignment.AssignedAt = DateTime.UtcNow;
            assignment.AssignedByUserId = ownerUserId.Value;
            assignment.RevokedAt = null;
        }

        user.UserType = "Staff";
        _dbContext.VenueAuditLogs.Add(NewAudit(venue.VenueId, ownerUserId.Value, $"StaffAssigned:{user.UserId}"));
        await _dbContext.SaveChangesAsync(cancellationToken);
        assignment.User = user;
        assignment.Venue = venue;

        return OwnerStaffMutationResult.Success(MapStaff(assignment));
    }

    public async Task<OwnerStaffMutationResult> CreateAccountAsync(
        CreateStaffAccountRequest request,
        int? ownerUserId,
        CancellationToken cancellationToken)
    {
        if (ownerUserId is null) return OwnerStaffMutationResult.Unauthorized();

        var venue = await _dbContext.Venues
            .SingleOrDefaultAsync(item => item.VenueId == request.VenueId && item.Owner.UserId == ownerUserId.Value, cancellationToken);
        if (venue is null) return OwnerStaffMutationResult.NotFound("Khong tim thay cum san thuoc tai khoan Owner.");

        var username = request.Username.Trim();
        var email = request.Email.Trim().ToLowerInvariant();
        if (await _dbContext.Users.AnyAsync(item => item.Email == email, cancellationToken))
            return OwnerStaffMutationResult.Conflict("Email da ton tai.");
        if (await _dbContext.Users.AnyAsync(item => item.Username == username, cancellationToken))
            return OwnerStaffMutationResult.Conflict("Ten dang nhap da duoc su dung.");

        var permissions = NormalizePermissions(request.Permissions);
        if (permissions.Count == 0) return OwnerStaffMutationResult.BadRequest("Hay cap it nhat mot quyen cho Staff.");

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
            AssignedByUserId = ownerUserId.Value
        };
        _dbContext.Users.Add(user);
        _dbContext.Staff.Add(assignment);
        _dbContext.VenueAuditLogs.Add(NewAudit(venue.VenueId, ownerUserId.Value, $"StaffAccountCreated:{email}"));
        await _dbContext.SaveChangesAsync(cancellationToken);

        return OwnerStaffMutationResult.Success(MapStaff(assignment));
    }

    public async Task<OwnerStaffMutationResult> UpdateAsync(
        int staffId,
        UpdateStaffRequest request,
        int? ownerUserId,
        CancellationToken cancellationToken)
    {
        if (ownerUserId is null) return OwnerStaffMutationResult.Unauthorized();

        var assignment = await _dbContext.Staff
            .Include(item => item.User)
            .Include(item => item.Venue)
            .SingleOrDefaultAsync(item => item.StaffId == staffId && item.Venue.Owner.UserId == ownerUserId.Value, cancellationToken);
        if (assignment is null) return OwnerStaffMutationResult.NotFound("Khong tim thay phan cong Staff.");

        var permissions = NormalizePermissions(request.Permissions);
        if (request.IsActive && permissions.Count == 0)
            return OwnerStaffMutationResult.BadRequest("Staff dang hoat dong can co it nhat mot quyen.");

        assignment.Role = CleanRole(request.Role);
        assignment.Permissions = string.Join(',', permissions);
        assignment.IsActive = request.IsActive;
        assignment.RevokedAt = request.IsActive ? null : DateTime.UtcNow;
        _dbContext.VenueAuditLogs.Add(NewAudit(
            assignment.VenueId,
            ownerUserId.Value,
            request.IsActive ? $"StaffPermissionsUpdated:{assignment.UserId}" : $"StaffRevoked:{assignment.UserId}"));
        await _dbContext.SaveChangesAsync(cancellationToken);

        return OwnerStaffMutationResult.Success(MapStaff(assignment));
    }

    public async Task<OwnerCheckInHistoryResult> GetCheckInHistoryAsync(
        int? venueId,
        DateOnly? date,
        int page,
        int pageSize,
        int? ownerUserId,
        CancellationToken cancellationToken)
    {
        if (ownerUserId is null) return OwnerCheckInHistoryResult.Unauthorized();

        var query = _dbContext.BookingOperations.AsNoTracking()
            .Where(item => item.Booking.Court.Venue.Owner.UserId == ownerUserId.Value);
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
            .Where(item => item.HasValue)
            .Select(item => item!.Value)
            .Distinct()
            .ToList();
        var actors = await _dbContext.Users.AsNoTracking()
            .Where(item => actorIds.Contains(item.UserId))
            .ToDictionaryAsync(item => item.UserId, item => item.Username, cancellationToken);

        return OwnerCheckInHistoryResult.Success(
            Pagination.Create(operations.Select(item => MapHistory(item, actors)), totalCount, page, pageSize));
    }

    private static string CleanRole(string? role) =>
        string.IsNullOrWhiteSpace(role) ? "Nhan vien van hanh" : role.Trim()[..Math.Min(role.Trim().Length, 100)];

    private static List<string> NormalizePermissions(IEnumerable<string>? values)
    {
        var permissions = (values ?? Array.Empty<string>())
            .Where(value => AllowedPermissions.Contains(value, StringComparer.OrdinalIgnoreCase))
            .Select(value => AllowedPermissions.First(item => item.Equals(value, StringComparison.OrdinalIgnoreCase)))
            .Distinct()
            .ToList();
        if (permissions.Count > 0 && !permissions.Contains("ViewBookings")) permissions.Insert(0, "ViewBookings");
        return permissions;
    }

    private static string[] SplitPermissions(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static VenueAuditLog NewAudit(int venueId, int actorId, string action) => new()
    {
        VenueId = venueId,
        ActorId = actorId,
        Action = action,
        Timestamp = DateTime.UtcNow
    };

    private static OwnerStaffResponse MapStaff(Staff item) => new()
    {
        StaffId = item.StaffId,
        UserId = item.UserId,
        Username = item.User.Username,
        Email = item.User.Email,
        VenueId = item.VenueId,
        VenueName = item.Venue.VenueName,
        Role = item.Role,
        Permissions = SplitPermissions(item.Permissions),
        IsActive = item.IsActive,
        AssignedAt = AsUtc(item.AssignedAt),
        RevokedAt = AsUtc(item.RevokedAt)
    };

    private static OwnerCheckInHistoryResponse MapHistory(BookingOperation item, IReadOnlyDictionary<int, string> actors) => new()
    {
        BookingId = item.BookingId,
        BookingCode = item.Booking.BookingCode ?? $"PL-{item.BookingId}",
        VenueId = item.Booking.Court.VenueId,
        VenueName = item.Booking.Court.Venue.VenueName,
        CourtNumber = item.Booking.Court.CourtNumber,
        PlayerName = item.Booking.Player?.User.Username ?? "Khach",
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

    private static string? ActorName(int? id, IReadOnlyDictionary<int, string> actors) =>
        id.HasValue && actors.TryGetValue(id.Value, out var name) ? name : null;

    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime? AsUtc(DateTime? value) => value.HasValue ? AsUtc(value.Value) : null;
}

public sealed record OwnerStaffListResult(
    OwnerStaffResultStatus Status,
    List<OwnerStaffResponse>? Staff,
    string? ErrorMessage)
{
    public static OwnerStaffListResult Success(List<OwnerStaffResponse> staff) =>
        new(OwnerStaffResultStatus.Success, staff, ErrorMessage: null);

    public static OwnerStaffListResult Unauthorized() =>
        new(OwnerStaffResultStatus.Unauthorized, Staff: null, ErrorMessage: null);
}

public sealed record OwnerStaffMutationResult(
    OwnerStaffResultStatus Status,
    OwnerStaffResponse? Staff,
    string? ErrorMessage)
{
    public static OwnerStaffMutationResult Success(OwnerStaffResponse staff) =>
        new(OwnerStaffResultStatus.Success, staff, ErrorMessage: null);

    public static OwnerStaffMutationResult Unauthorized() =>
        new(OwnerStaffResultStatus.Unauthorized, Staff: null, ErrorMessage: null);

    public static OwnerStaffMutationResult BadRequest(string errorMessage) =>
        new(OwnerStaffResultStatus.BadRequest, Staff: null, errorMessage);

    public static OwnerStaffMutationResult NotFound(string errorMessage) =>
        new(OwnerStaffResultStatus.NotFound, Staff: null, errorMessage);

    public static OwnerStaffMutationResult Conflict(string errorMessage) =>
        new(OwnerStaffResultStatus.Conflict, Staff: null, errorMessage);
}

public sealed record OwnerCheckInHistoryResult(
    OwnerStaffResultStatus Status,
    PaginatedResponse<OwnerCheckInHistoryResponse>? History,
    string? ErrorMessage)
{
    public static OwnerCheckInHistoryResult Success(PaginatedResponse<OwnerCheckInHistoryResponse> history) =>
        new(OwnerStaffResultStatus.Success, history, ErrorMessage: null);

    public static OwnerCheckInHistoryResult Unauthorized() =>
        new(OwnerStaffResultStatus.Unauthorized, History: null, ErrorMessage: null);
}

public enum OwnerStaffResultStatus
{
    Success,
    BadRequest,
    Unauthorized,
    NotFound,
    Conflict
}