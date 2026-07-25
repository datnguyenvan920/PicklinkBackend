using Microsoft.EntityFrameworkCore;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Repositories;
using PicklinkBackend.Services.Auth;
using PicklinkBackend.Services.Owner;

namespace PicklinkBackend.Services.Owner.Implementations;

public sealed class OwnerStaffService
{
    public static readonly string[] AllowedPermissions =
    {
        "ViewBookings", "VerifyBooking", "ConfirmPayment", "CheckIn", "MarkNoShow"
    };

    private readonly IVenueRepository _venueRepository;
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;

    public OwnerStaffService(
        IVenueRepository venueRepository,
        IUserRepository userRepository,
        IPasswordHasher passwordHasher)
    {
        _venueRepository = venueRepository;
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
    }

    public async Task<OwnerStaffListResult> ListAsync(int? ownerUserId, CancellationToken cancellationToken)
    {
        if (ownerUserId is null) return OwnerStaffListResult.Unauthorized();

        var assignments = await _venueRepository.Staff.AsNoTracking()
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

        var venueIds = NormalizeVenueIds(request.VenueIds, request.VenueId);
        if (venueIds.Count == 0)
            return OwnerStaffMutationResult.BadRequest("Hay chon it nhat mot cum san cho Staff.");
        var venues = await _venueRepository.Venues
            .Where(item => venueIds.Contains(item.VenueId) && item.Owner.UserId == ownerUserId.Value)
            .OrderBy(item => item.VenueId)
            .ToListAsync(cancellationToken);
        if (venues.Count != venueIds.Count)
            return OwnerStaffMutationResult.NotFound("Co cum san khong thuoc tai khoan Owner.");

        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _userRepository.GetByEmailAsync(email, cancellationToken);
        if (user is null) return OwnerStaffMutationResult.NotFound("Khong tim thay tai khoan voi email nay.");
        if (user.UserId == ownerUserId.Value) return OwnerStaffMutationResult.Conflict("Owner khong the tu gan minh lam Staff.");
        if (user.UserType is "Admin" or "VenueOwner") return OwnerStaffMutationResult.Conflict("Khong the gan tai khoan Admin hoac Owner lam Staff.");

        var permissions = NormalizePermissions(request.Permissions);
        if (permissions.Count == 0) return OwnerStaffMutationResult.BadRequest("Hay cap it nhat mot quyen cho Staff.");
        var permissionValue = string.Join(',', permissions);
        var existingAssignments = await _venueRepository.Staff
            .Include(item => item.User)
            .Include(item => item.Venue)
            .Where(item => item.UserId == user.UserId && venueIds.Contains(item.VenueId))
            .ToListAsync(cancellationToken);
        var updatedAssignments = new List<PicklinkBackend.Models.Staff>();
        var now = DateTime.UtcNow;

        foreach (var venue in venues)
        {
            var assignment = existingAssignments.SingleOrDefault(item => item.VenueId == venue.VenueId);
            if (assignment is null)
            {
                assignment = new PicklinkBackend.Models.Staff
                {
                    User = user,
                    UserId = user.UserId,
                    Venue = venue,
                    VenueId = venue.VenueId,
                    Role = CleanRole(request.Role),
                    Permissions = permissionValue,
                    IsActive = true,
                    AssignedAt = now,
                    AssignedByUserId = ownerUserId.Value
                };
                await _venueRepository.AddStaffAsync(assignment, cancellationToken);
            }
            else
            {
                assignment.Role = CleanRole(request.Role);
                assignment.Permissions = permissionValue;
                assignment.IsActive = true;
                assignment.AssignedAt = now;
                assignment.AssignedByUserId = ownerUserId.Value;
                assignment.RevokedAt = null;
            }

            updatedAssignments.Add(assignment);
            await _venueRepository.AddAuditLogAsync(NewAudit(venue.VenueId, ownerUserId.Value, $"StaffAssigned:{user.UserId}"), cancellationToken);
        }

        user.UserType = "Staff";
        await _venueRepository.SaveChangesAsync(cancellationToken);
        return OwnerStaffMutationResult.Success(MapStaff(updatedAssignments[0]));
    }

    public async Task<OwnerStaffMutationResult> CreateAccountAsync(
        CreateStaffAccountRequest request,
        int? ownerUserId,
        CancellationToken cancellationToken)
    {
        if (ownerUserId is null) return OwnerStaffMutationResult.Unauthorized();

        var venueIds = NormalizeVenueIds(request.VenueIds, request.VenueId);
        if (venueIds.Count == 0)
            return OwnerStaffMutationResult.BadRequest("Hay chon it nhat mot cum san cho Staff.");
        var venues = await _venueRepository.Venues
            .Where(item => venueIds.Contains(item.VenueId) && item.Owner.UserId == ownerUserId.Value)
            .OrderBy(item => item.VenueId)
            .ToListAsync(cancellationToken);
        if (venues.Count != venueIds.Count)
            return OwnerStaffMutationResult.NotFound("Co cum san khong thuoc tai khoan Owner.");

        var username = request.Username.Trim();
        var email = request.Email.Trim().ToLowerInvariant();
        if (await _userRepository.EmailExistsAsync(email, cancellationToken))
            return OwnerStaffMutationResult.Conflict("Email da ton tai.");
        if (await _userRepository.UsernameExistsAsync(username, cancellationToken))
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
        var now = DateTime.UtcNow;
        var assignments = venues.Select(venue => new PicklinkBackend.Models.Staff
        {
            User = user,
            Venue = venue,
            Role = CleanRole(request.Role),
            Permissions = string.Join(',', permissions),
            IsActive = true,
            AssignedAt = now,
            AssignedByUserId = ownerUserId.Value
        }).ToList();
        await _userRepository.AddUserAsync(user, cancellationToken);
        await _venueRepository.AddStaffRangeAsync(assignments, cancellationToken);
        foreach (var venue in venues)
            await _venueRepository.AddAuditLogAsync(NewAudit(venue.VenueId, ownerUserId.Value, $"StaffAccountCreated:{email}"), cancellationToken);
        await _venueRepository.SaveChangesAsync(cancellationToken);

        return OwnerStaffMutationResult.Success(MapStaff(assignments[0]));
    }

    public async Task<OwnerStaffMutationResult> UpdateAsync(
        int staffId,
        UpdateStaffRequest request,
        int? ownerUserId,
        CancellationToken cancellationToken)
    {
        if (ownerUserId is null) return OwnerStaffMutationResult.Unauthorized();

        var assignment = await _venueRepository.Staff
            .Include(item => item.User)
            .Include(item => item.Venue)
            .SingleOrDefaultAsync(item => item.StaffId == staffId && item.Venue.Owner.UserId == ownerUserId.Value, cancellationToken);
        if (assignment is null) return OwnerStaffMutationResult.NotFound("Khong tim thay phan cong Staff.");

        var username = request.Username?.Trim();
        var email = request.Email?.Trim().ToLowerInvariant();
        var updatesAccountIdentity =
            (username is not null && !string.Equals(username, assignment.User.Username, StringComparison.Ordinal))
            || (email is not null && !string.Equals(email, assignment.User.Email, StringComparison.OrdinalIgnoreCase));
        if (updatesAccountIdentity && await _venueRepository.Staff.AnyAsync(
                item => item.UserId == assignment.UserId
                    && item.Venue.Owner.UserId != ownerUserId.Value,
                cancellationToken))
            return OwnerStaffMutationResult.Conflict(
                "Tai khoan Staff dang duoc phan cong cho Owner khac nen khong the doi ten dang nhap hoac email.");

        if (username is not null)
        {
            if (username.Length < 3)
                return OwnerStaffMutationResult.BadRequest("Ten dang nhap phai co it nhat 3 ky tu.");
            if (await _userRepository.Users.AnyAsync(
                    item => item.UserId != assignment.UserId && item.Username == username,
                    cancellationToken))
                return OwnerStaffMutationResult.Conflict("Ten dang nhap da duoc su dung.");
            assignment.User.Username = username;
        }

        if (email is not null)
        {
            if (string.IsNullOrWhiteSpace(email))
                return OwnerStaffMutationResult.BadRequest("Email Staff khong hop le.");
            if (await _userRepository.Users.AnyAsync(
                    item => item.UserId != assignment.UserId && item.Email == email,
                    cancellationToken))
                return OwnerStaffMutationResult.Conflict("Email da ton tai.");
            assignment.User.Email = email;
        }

        var permissions = NormalizePermissions(request.Permissions);
        if (request.IsActive && permissions.Count == 0)
            return OwnerStaffMutationResult.BadRequest("Staff dang hoat dong can co it nhat mot quyen.");

        var hasVenueSelection = request.VenueIds is not null || request.VenueId.HasValue;
        var selectedVenueIds = hasVenueSelection
            ? NormalizeVenueIds(request.VenueIds, request.VenueId)
            : [assignment.VenueId];
        if (selectedVenueIds.Count == 0)
            return OwnerStaffMutationResult.BadRequest("Hay chon it nhat mot cum san cho Staff.");

        var selectedVenues = await _venueRepository.Venues
            .Where(item => selectedVenueIds.Contains(item.VenueId) && item.Owner.UserId == ownerUserId.Value)
            .OrderBy(item => item.VenueId)
            .ToListAsync(cancellationToken);
        if (selectedVenues.Count != selectedVenueIds.Count)
            return OwnerStaffMutationResult.NotFound("Co cum san khong thuoc tai khoan Owner.");

        var ownerAssignments = hasVenueSelection
            ? await _venueRepository.Staff
                .Include(item => item.User)
                .Include(item => item.Venue)
                .Where(item => item.UserId == assignment.UserId && item.Venue.Owner.UserId == ownerUserId.Value)
                .ToListAsync(cancellationToken)
            : [assignment];
        var targetAssignments = new List<PicklinkBackend.Models.Staff>();
        var now = DateTime.UtcNow;
        var role = CleanRole(request.Role);
        var permissionValue = string.Join(',', permissions);

        foreach (var venue in selectedVenues)
        {
            var target = ownerAssignments.SingleOrDefault(item => item.VenueId == venue.VenueId);
            if (target is null)
            {
                target = new PicklinkBackend.Models.Staff
                {
                    User = assignment.User,
                    UserId = assignment.UserId,
                    Venue = venue,
                    VenueId = venue.VenueId,
                    AssignedAt = now,
                    AssignedByUserId = ownerUserId.Value
                };
                await _venueRepository.AddStaffAsync(target, cancellationToken);
                ownerAssignments.Add(target);
            }

            var wasActive = target.IsActive;
            target.Role = role;
            target.Permissions = permissionValue;
            target.IsActive = request.IsActive;
            target.RevokedAt = request.IsActive ? null : now;
            if (request.IsActive && !wasActive)
            {
                target.AssignedAt = now;
                target.AssignedByUserId = ownerUserId.Value;
            }
            targetAssignments.Add(target);
            await _venueRepository.AddAuditLogAsync(NewAudit(
                venue.VenueId,
                ownerUserId.Value,
                request.IsActive ? $"StaffUpdated:{assignment.UserId}" : $"StaffRevoked:{assignment.UserId}"), cancellationToken);
        }

        if (hasVenueSelection)
        {
            var selectedSet = selectedVenueIds.ToHashSet();
            foreach (var removed in ownerAssignments.Where(item => !selectedSet.Contains(item.VenueId) && item.IsActive))
            {
                removed.IsActive = false;
                removed.RevokedAt = now;
                await _venueRepository.AddAuditLogAsync(NewAudit(
                    removed.VenueId,
                    ownerUserId.Value,
                    $"StaffRevoked:{assignment.UserId}"), cancellationToken);
            }
        }

        await _venueRepository.SaveChangesAsync(cancellationToken);
        return OwnerStaffMutationResult.Success(MapStaff(targetAssignments[0]));
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

        var query = _venueRepository.BookingOperations.AsNoTracking()
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
        var actors = await _userRepository.Users.AsNoTracking()
            .Where(item => actorIds.Contains(item.UserId))
            .ToDictionaryAsync(item => item.UserId, item => item.Username, cancellationToken);

        return OwnerCheckInHistoryResult.Success(
            Pagination.Create(operations.Select(item => MapHistory(item, actors)), totalCount, page, pageSize));
    }

    private static List<int> NormalizeVenueIds(IEnumerable<int>? values, int? fallbackVenueId)
    {
        var venueIds = (values ?? Array.Empty<int>())
            .Where(value => value > 0)
            .Distinct()
            .ToList();
        if (venueIds.Count == 0 && fallbackVenueId.GetValueOrDefault() > 0)
            venueIds.Add(fallbackVenueId!.Value);
        return venueIds;
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

    private static OwnerStaffAccountResponse MapStaff(PicklinkBackend.Models.Staff item) => new()
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
