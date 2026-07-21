using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Services.Schedules;
using PicklinkBackend.Services.Shared;

namespace PicklinkBackend.Services.Bookings;
public sealed record PlayerBookingServiceDependencies(ApplicationDbContext DbContext, IConfiguration Configuration, ScheduleRealtimeNotifier ScheduleRealtime, PlayerScheduleConflictService PlayerScheduleConflict);

public class PlayerBookingService
{
    private static readonly string[] InactiveStatuses = ["Cancelled", "Expired"];
    private const int MaximumAdvanceBookingMonths = 12;
    private readonly ApplicationDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly ScheduleRealtimeNotifier _scheduleRealtime;
    private readonly PlayerScheduleConflictService _playerScheduleConflict;

    public PlayerBookingService(
        ApplicationDbContext dbContext,
        IConfiguration configuration,
        ScheduleRealtimeNotifier scheduleRealtime,
        PlayerScheduleConflictService playerScheduleConflict)
    {
        _dbContext = dbContext;
        _configuration = configuration;
        _scheduleRealtime = scheduleRealtime;
        _playerScheduleConflict = playerScheduleConflict;
    }
    private static ServiceResult Ok(object? value = null) =>
        new(ServiceResultStatus.Success, value);

    private static ServiceResult NoContent() =>
        new(ServiceResultStatus.NoContent);

    private static ServiceResult BadRequest(object? error = null) =>
        new(ServiceResultStatus.BadRequest, Error: error);

    private static ServiceResult Unauthorized(object? error = null) =>
        new(ServiceResultStatus.Unauthorized, Error: error);

    private static ServiceResult Forbid(object? error = null) =>
        new(ServiceResultStatus.Forbidden, Error: error);

    private static ServiceResult NotFound(object? error = null) =>
        new(ServiceResultStatus.NotFound, Error: error);

    private static ServiceResult Conflict(object? error = null) =>
        new(ServiceResultStatus.Conflict, Error: error);

    private static ServiceResult StatusCode(int statusCode, object? body = null) =>
        statusCode >= 400
            ? new(ServiceResultStatus.StatusCode, Error: body, RawStatusCode: statusCode)
            : new(ServiceResultStatus.StatusCode, Value: body, RawStatusCode: statusCode);
    public async Task<ServiceResult<PaginatedResponse<PlayerVenueSummaryResponse>>> GetVenues(
        string? search,
        string? area,
        decimal? minPrice,
        decimal? maxPrice,
        bool favoritesOnly = false,
        int page = 1,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        if (minPrice is < 0 || maxPrice is < 0 || (minPrice.HasValue && maxPrice.HasValue && minPrice > maxPrice))
            return BadRequest(new { message = "Khoảng giá không hợp lệ." });

        var userId = CurrentUserId();
        var favoriteVenueIds = userId.HasValue
            ? await _dbContext.FavoriteVenues.AsNoTracking()
                .Where(item => item.Player.UserId == userId.Value)
                .Select(item => item.VenueId)
                .ToListAsync(cancellationToken)
            : [];
        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);
        if (favoritesOnly && favoriteVenueIds.Count == 0)
            return Ok(Pagination.Create(Array.Empty<PlayerVenueSummaryResponse>(), 0, page, pageSize));

        var keyword = search?.Trim();
        var normalizedArea = area?.Trim();
        var venueQuery = _dbContext.Venues.AsNoTracking()
            .Where(venue => venue.ApprovalStatus == "Approved");
        if (!string.IsNullOrWhiteSpace(keyword))
            venueQuery = venueQuery.Where(venue => venue.VenueName.Contains(keyword) || venue.Address.Contains(keyword));
        if (!string.IsNullOrWhiteSpace(normalizedArea))
            venueQuery = venueQuery.Where(venue => venue.Address.Contains(normalizedArea));
        if (favoritesOnly)
            venueQuery = venueQuery.Where(venue => favoriteVenueIds.Contains(venue.VenueId));

        var venueRows = await venueQuery
            .Select(venue => new
            {
                venue.VenueId,
                venue.VenueName,
                venue.Address,
                venue.Latitude,
                venue.Longitude,
                venue.OverallRating,
                venue.OpenTime,
                venue.CloseTime,
                ImageUrl = venue.VenueImages
                    .OrderByDescending(image => image.IsPrimary)
                    .ThenBy(image => image.SortOrder)
                    .Select(image => image.ImageUrl)
                    .FirstOrDefault(),
                BasePriceText = venue.BookingRules
                    .Where(rule => rule.RuleType == "BasePrice")
                    .Select(rule => rule.RuleContent)
                    .FirstOrDefault(),
                AvailableCourtPrices = venue.Courts
                    .Where(court => court.AvailabilityStatus == "Available" && court.HourlyPrice > 0)
                    .Select(court => court.HourlyPrice)
                    .ToList(),
                CourtCount = venue.Courts.Count(court => court.AvailabilityStatus == "Available")
            })
            .ToListAsync(cancellationToken);
        var favoriteVenueLookup = favoriteVenueIds.ToHashSet();
        var response = venueRows.Select(venue =>
        {
            var basePrice = decimal.TryParse(venue.BasePriceText, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : 0;
            var fromPrice = venue.AvailableCourtPrices.DefaultIfEmpty(basePrice).Min();
            return new PlayerVenueSummaryResponse
            {
                VenueId = venue.VenueId,
                VenueName = venue.VenueName,
                Address = venue.Address,
                Latitude = venue.Latitude,
                Longitude = venue.Longitude,
                OverallRating = venue.OverallRating,
                OpenTime = venue.OpenTime.ToString("HH:mm"),
                CloseTime = venue.CloseTime.ToString("HH:mm"),
                ImageUrl = venue.ImageUrl,
                FromPrice = fromPrice,
                CourtCount = venue.CourtCount,
                IsFavorite = favoriteVenueLookup.Contains(venue.VenueId)
            };
        })
        .Where(venue => !minPrice.HasValue || venue.FromPrice >= minPrice.Value)
        .Where(venue => !maxPrice.HasValue || venue.FromPrice <= maxPrice.Value)
        .OrderByDescending(venue => venue.IsFavorite)
        .ThenBy(venue => venue.VenueName)
        .ToList();

        var totalCount = response.Count;
        var items = response.Skip((page - 1) * pageSize).Take(pageSize);
        return Ok(Pagination.Create(items, totalCount, page, pageSize));
    }
    public Task<ServiceResult<PaginatedResponse<PlayerVenueSummaryResponse>>> GetFavoriteVenues(
        int page = 1,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default) =>
        GetVenues(null, null, null, null, true, page, pageSize, cancellationToken);
    public async Task<ServiceResult> AddFavoriteVenue(int venueId, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var player = await GetOrCreatePlayerAsync(userId.Value, cancellationToken);
        if (player is null) return Forbid();
        if (!await _dbContext.Venues
            .AnyAsync(
                item => item.VenueId == venueId
                    && item.ApprovalStatus == "Approved",
                cancellationToken))
            return NotFound(new { message = "Không tìm thấy cụm sân." });
        if (!await _dbContext.FavoriteVenues.AnyAsync(item => item.PlayerId == player.PlayerId && item.VenueId == venueId, cancellationToken))
        {
            _dbContext.FavoriteVenues.Add(new FavoriteVenue
            {
                PlayerId = player.PlayerId,
                VenueId = venueId,
                CreatedAt = DateTime.UtcNow
            });
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                _dbContext.ChangeTracker.Clear();
                if (!await _dbContext.FavoriteVenues.AsNoTracking()
                    .AnyAsync(item => item.PlayerId == player.PlayerId && item.VenueId == venueId, cancellationToken))
                    throw;
            }
        }
        return NoContent();
    }
    public async Task<ServiceResult> RemoveFavoriteVenue(int venueId, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var favorite = await _dbContext.FavoriteVenues
            .SingleOrDefaultAsync(item => item.VenueId == venueId && item.Player.UserId == userId.Value, cancellationToken);
        if (favorite is not null)
        {
            _dbContext.FavoriteVenues.Remove(favorite);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        return NoContent();
    }
    public async Task<ServiceResult<PlayerCourtAvailabilityResponse>> GetAvailability(
        int venueId,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var venue = await _dbContext.Venues.AsNoTracking()
            .AsSplitQuery()
            .Include(item => item.Courts)
            .Include(item => item.BookingRules)
            .SingleOrDefaultAsync(
                venue => venue.VenueId == venueId
                    && venue.ApprovalStatus == "Approved",
                cancellationToken);
        if (venue is null) return NotFound(new { message = "Không tìm thấy cụm sân." });

        var dayStart = date.ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);
        var currentUserId = CurrentUserId();
        var now = DateTime.UtcNow;
        var bookings = await _dbContext.Bookings.AsNoTracking()
            .Where(booking => booking.Court.VenueId == venueId && booking.StartTime < dayEnd && booking.EndTime > dayStart &&
                !InactiveStatuses.Contains(booking.Status) && (booking.Status != "Holding" || booking.HoldExpiresAt > now))
            .Include(booking => booking.Player)
            .Include(booking => booking.Slots)
            .ToListAsync(cancellationToken);

        var response = new PlayerCourtAvailabilityResponse
        {
            VenueId = venue.VenueId,
            VenueName = venue.VenueName,
            Address = venue.Address,
            OpenTime = venue.OpenTime.ToString("HH:mm"),
            CloseTime = venue.CloseTime.ToString("HH:mm"),
            Date = date,
            Courts = venue.Courts.Where(court => court.AvailabilityStatus != "Inactive").OrderBy(court => court.CourtNumber).Select(court => new PlayerCourtResponse
            {
                CourtId = court.CourtId,
                CourtNumber = court.CourtNumber,
                CourtType = court.CourtType ?? "Tiêu chuẩn",
                SurfaceType = court.SurfaceType,
                IsIndoor = court.IsIndoor,
                HourlyPrice = court.HourlyPrice > 0 ? court.HourlyPrice : GetBasePrice(venue)
            }).ToList()
        };

        foreach (var court in venue.Courts.Where(item => item.AvailabilityStatus != "Inactive"))
        {
            var opening = date.ToDateTime(venue.OpenTime);
            var closing = date.ToDateTime(venue.CloseTime);
            for (var start = opening; start.AddMinutes(30) <= closing; start = start.AddMinutes(30))
            {
                var end = start.AddMinutes(30);
                var overlap = bookings.FirstOrDefault(booking =>
                    booking.Slots.Any(slot => slot.CourtId == court.CourtId && slot.StartTime < end && slot.EndTime > start)
                    || (!booking.Slots.Any() && booking.CourtId == court.CourtId && booking.StartTime < end && booking.EndTime > start));
                var status = !venue.IsOpen ? "Closed"
                    : court.AvailabilityStatus == "Maintenance" ? "Maintenance"
                    : overlap is null ? "Available"
                    : overlap.Status == "Holding" ? "Holding"
                    : overlap.PlayerId is not null ? "Booked"
                    : overlap.OwnerEntryType ?? "Blocked";
                var isOwnedHolding = overlap?.Status == "Holding"
                    && currentUserId.HasValue
                    && overlap.Player?.UserId == currentUserId.Value;
                response.Slots.Add(new PlayerAvailabilitySlotResponse
                {
                    CourtId = court.CourtId,
                    StartTime = start,
                    EndTime = end,
                    Status = status,
                    BookingId = isOwnedHolding ? overlap!.BookingId : null,
                    IsOwnedByCurrentUser = isOwnedHolding
                });
            }
        }

        return Ok(response);
    }
    public async Task<ServiceResult<BookingHoldingResponse>> CreateHolding(
        CreateBookingHoldRequest request,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var player = await GetOrCreatePlayerAsync(userId.Value, cancellationToken);
        if (player is null) return BadRequest(new { message = "Tài khoản chưa có hồ sơ người chơi." });

        var bookingDate = DateOnly.FromDateTime(VietnamTime.Now);
        var maxBookingDate = bookingDate.AddMonths(MaximumAdvanceBookingMonths);
        if (request.Date < bookingDate || request.Date > maxBookingDate)
            return BadRequest(new { message = $"Người chơi chỉ được đặt sân trong vòng {MaximumAdvanceBookingMonths} tháng kể từ hôm nay." });

        var selectedSlots = request.Slots
            .Select(item => new { item.CourtId, item.StartTime, Date = item.Date ?? request.Date })
            .OrderBy(item => item.Date)
            .ThenBy(item => item.CourtId)
            .ThenBy(item => item.StartTime)
            .ToList();
        if (selectedSlots.Count == 0
            || selectedSlots.DistinctBy(item => new { item.Date, item.CourtId, item.StartTime }).Count() != request.Slots.Count)
            return BadRequest(new { message = "Danh sách slot bị trùng." });
        if (selectedSlots.Any(slot => slot.Date < bookingDate || slot.Date > maxBookingDate))
            return BadRequest(new { message = $"Người chơi chỉ được đặt sân trong vòng {MaximumAdvanceBookingMonths} tháng kể từ hôm nay." });
        if (selectedSlots.Any(slot => slot.StartTime.Minute % 30 != 0 || slot.StartTime.Second != 0))
            return BadRequest(new { message = "Slot phải bắt đầu tại phút 00 hoặc 30." });

        var selectedRanges = selectedSlots.Select(slot => new
        {
            slot.CourtId,
            Start = slot.Date.ToDateTime(slot.StartTime),
            End = slot.Date.ToDateTime(slot.StartTime).AddMinutes(30)
        }).OrderBy(slot => slot.Start).ThenBy(slot => slot.CourtId).ToList();
        if (selectedRanges.Where((slot, index) => selectedRanges.Take(index)
                .Any(other => slot.Start < other.End && slot.End > other.Start)).Any())
            return BadRequest(new { message = "Moi khung gio chi duoc chon mot san con." });
        var selectedCourtIds = selectedSlots.Select(item => item.CourtId).Distinct().ToList();
        if (selectedRanges.Any(slot => slot.Start <= VietnamTime.Now))
            return BadRequest(new { message = "Không thể giữ chỗ cho khung giờ đã qua." });

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        foreach (var courtId in selectedCourtIds.OrderBy(item => item))
        {
            if (!await SqlServerBookingLock.AcquireAsync(_dbContext, transaction, $"court-booking:{courtId}", cancellationToken))
                return Conflict(new { message = "Sân đang được người khác thao tác. Vui lòng thử lại." });
        }
        if (!await SqlServerBookingLock.AcquireAsync(_dbContext, transaction, $"player-schedule:{player.PlayerId}", cancellationToken))
            return Conflict(new { message = "Lịch của bạn đang được cập nhật. Vui lòng thử lại." });

        var courts = await _dbContext.Courts
            .Include(item => item.Venue).ThenInclude(venue => venue.BookingRules)
            .Where(item => selectedCourtIds.Contains(item.CourtId))
            .ToListAsync(cancellationToken);
        if (courts.Count != selectedCourtIds.Count) return NotFound(new { message = "Không tìm thấy sân con." });
        if (courts.Select(item => item.VenueId).Distinct().Skip(1).Any())
            return BadRequest(new { message = "Các slot phải thuộc cùng một cụm sân." });
        var venue = courts[0].Venue;
        var court = courts[0];
        if (venue.ApprovalStatus != "Approved"
            || !venue.IsOpen
            || courts.Any(court => court.AvailabilityStatus != "Available"))
            return Conflict(new { message = "Sân hiện không nhận đặt chỗ." });

        var courtsById = courts.ToDictionary(item => item.CourtId);
        if (selectedRanges.Any(slot => TimeOnly.FromDateTime(slot.Start) < venue.OpenTime
            || TimeOnly.FromDateTime(slot.End) > venue.CloseTime))
            return BadRequest(new { message = $"Khung giờ phải nằm trong giờ mở cửa {court.Venue.OpenTime:HH:mm}–{court.Venue.CloseTime:HH:mm}." });

        var utcNow = DateTime.UtcNow;
        var firstStartTime = selectedRanges.Min(item => item.Start);
        var lastEndTime = selectedRanges.Max(item => item.End);
        var staleHoldings = await _dbContext.Bookings
            .Include(booking => booking.Payments).ThenInclude(payment => payment.StatusHistories)
            .Where(booking => selectedCourtIds.Contains(booking.CourtId) && booking.Status == "Holding" && booking.HoldExpiresAt <= utcNow)
            .ToListAsync(cancellationToken);
        foreach (var stale in staleHoldings) await ExpireHoldingAsync(stale, "Hết thời gian giữ chỗ", cancellationToken);
        if (staleHoldings.Count > 0) await _dbContext.SaveChangesAsync(cancellationToken);

        if (!request.AllowScheduleConflicts)
        {
            var playerName = await _dbContext.Users.AsNoTracking()
                .Where(item => item.UserId == player.UserId)
                .Select(item => item.Username)
                .SingleOrDefaultAsync(cancellationToken) ?? "Bạn";
            var conflictDetails = await _playerScheduleConflict.LoadConflictDetailsAsync(
                player.PlayerId,
                firstStartTime,
                lastEndTime,
                cancellationToken: cancellationToken);
            var scheduleConflicts = new List<object>();
            foreach (var slot in selectedRanges)
            foreach (var conflict in conflictDetails.Where(conflict => conflict.StartTime < slot.End && conflict.EndTime > slot.Start))
                scheduleConflicts.Add(new
                {
                    playerName,
                    selectedSlot = new
                    {
                        venueName = venue.VenueName,
                        courtNumber = courtsById[slot.CourtId].CourtNumber,
                        startTime = slot.Start,
                        endTime = slot.End
                    },
                    conflictingSlot = conflict
                });

            if (scheduleConflicts.Count > 0)
                return Conflict(new
                {
                    message = "Bạn đã có lịch trùng với slot được chọn.",
                    requiresScheduleConflictConfirmation = true,
                    conflicts = scheduleConflicts.Distinct()
                });
        }
        var possiblyOverlappingBookings = await _dbContext.Bookings
            .Where(booking =>
                !InactiveStatuses.Contains(booking.Status) &&
                (booking.Status != "Holding" || booking.HoldExpiresAt > utcNow) &&
                booking.StartTime < lastEndTime && booking.EndTime > firstStartTime &&
                (selectedCourtIds.Contains(booking.CourtId) || booking.Slots.Any(existingSlot => selectedCourtIds.Contains(existingSlot.CourtId))))
            .Include(booking => booking.Slots)
            .ToListAsync(cancellationToken);
        var overlaps = possiblyOverlappingBookings.Any(booking => selectedRanges.Any(slot =>
            booking.Slots.Any(existingSlot => existingSlot.CourtId == slot.CourtId && existingSlot.StartTime < slot.End && existingSlot.EndTime > slot.Start)
            || (!booking.Slots.Any() && booking.CourtId == slot.CourtId && booking.StartTime < slot.End && booking.EndTime > slot.Start)));
        if (overlaps) return Conflict(new { message = "Một hoặc nhiều slot vừa được người khác giữ. Hãy tải lại lịch." });

        var holdMinutes = Math.Clamp(_configuration.GetValue("Booking:HoldingMinutes", 5), 1, 60);
        var bankAccount = await _dbContext.OwnerBankAccounts.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OwnerId == venue.OwnerId && item.IsActive, cancellationToken);
        var paymentGroupId = Guid.NewGuid();
        var groupTransferContent = $"PLG-{paymentGroupId:N}"[..20].ToUpperInvariant();
        var groupTotal = selectedRanges.Sum(slot =>
        {
            var selectedCourt = courtsById[slot.CourtId];
            var hourlyPrice = selectedCourt.HourlyPrice > 0 ? selectedCourt.HourlyPrice : GetBasePrice(venue);
            return RoundMoney(hourlyPrice * (decimal)(slot.End - slot.Start).TotalHours);
        });

        var parentRange = selectedRanges[0];
        var parentCourt = courtsById[parentRange.CourtId];
        var booking = new Booking
        {
            PlayerId = player.PlayerId,
            CourtId = parentCourt.CourtId,
            StartTime = parentRange.Start,
            EndTime = selectedRanges.Max(slot => slot.End),
            Status = "Holding",
            BookingCode = $"PL-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            CreatedAt = utcNow,
            HoldExpiresAt = utcNow.AddMinutes(holdMinutes),
            HourlyPriceSnapshot = parentCourt.HourlyPrice > 0 ? parentCourt.HourlyPrice : GetBasePrice(venue),
            CourtAmount = groupTotal,
            TotalAmount = groupTotal
        };
        BookingCheckInGroup? currentCheckInGroup = null;
        foreach (var selectedSlot in selectedRanges)
        {
            var selectedCourt = courtsById[selectedSlot.CourtId];
            var startsNewCheckInGroup = currentCheckInGroup is null
                || currentCheckInGroup.CourtId != selectedSlot.CourtId
                || currentCheckInGroup.EndTime != selectedSlot.Start;
            if (startsNewCheckInGroup)
            {
                currentCheckInGroup = new BookingCheckInGroup
                {
                    CourtId = selectedSlot.CourtId,
                    Court = selectedCourt,
                    StartTime = selectedSlot.Start,
                    EndTime = selectedSlot.End,
                    CheckInCode = $"CI-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
                    UpdatedAt = utcNow
                };
                booking.CheckInGroups.Add(currentCheckInGroup);
            }
            else if (currentCheckInGroup is not null) currentCheckInGroup.EndTime = selectedSlot.End;

            var durationHours = (selectedSlot.End - selectedSlot.Start).TotalHours;
            var hourlyPrice = selectedCourt.HourlyPrice > 0 ? selectedCourt.HourlyPrice : GetBasePrice(venue);
            booking.Slots.Add(new BookingSlot
            {
                CourtId = selectedCourt.CourtId,
                Court = selectedCourt,
                StartTime = selectedSlot.Start,
                EndTime = selectedSlot.End,
                HourlyPriceSnapshot = hourlyPrice,
                CourtAmount = RoundMoney(hourlyPrice * (decimal)durationHours),
                CheckInGroup = currentCheckInGroup
            });
        }

        booking.StatusHistories.Add(NewHistory(null, "Holding", "Player \u0074\u1ea1o \u0067\u0069\u1eef \u0063\u0068\u1ed7", userId));
        var payment = new Payment
        {
            PayerId = player.PlayerId,
            PaymentGroupId = paymentGroupId,
            Amount = groupTotal,
            PaymentMethod = "BankTransfer",
            Status = "Pending",
            TransferCode = booking.BookingCode!.Replace("-", string.Empty),
            TransferContent = groupTransferContent,
            BankCode = bankAccount?.BankCode,
            BankName = bankAccount?.BankName,
            BankAccountNumber = bankAccount?.AccountNumber,
            BankAccountName = bankAccount?.AccountHolderName,
            QrImageUrl = bankAccount is null ? null : BuildVietQrUrl(bankAccount, groupTotal, groupTransferContent)
        };
        payment.StatusHistories.Add(NewPaymentHistory(null, "Pending", "Created", "T\u1ea1o y\u00eau c\u1ea7u chuy\u1ec3n kho\u1ea3n", userId));
        booking.Payments.Add(payment);
        _dbContext.Bookings.Add(booking);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        foreach (var slot in booking.Slots)
            _scheduleRealtime.Publish(new ScheduleChangedEvent(venue.VenueId, slot.CourtId, slot.StartTime, slot.EndTime, "Holding", "Created"));

        return Ok(MapBooking(booking, parentCourt));
    }
    public async Task<ServiceResult<PaginatedResponse<BookingHoldingResponse>>> GetMyBookings(
        int page = 1,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);
        var query = _dbContext.Bookings.AsNoTracking()
            .AsSplitQuery()
            .Where(booking => booking.Player != null && booking.Player.UserId == userId);
        var totalCount = await query.CountAsync(cancellationToken);
        var localNow = VietnamTime.Now;
        var utcNow = DateTime.UtcNow;
        var bookings = await query
            .OrderByDescending(booking => booking.StartTime)
            .ThenByDescending(booking => booking.BookingId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(booking => new BookingHoldingResponse
            {
                BookingId = booking.BookingId,
                BookingCode = booking.BookingCode ?? string.Empty,
                Status = booking.Status,
                CreatedAt = booking.CreatedAt,
                HoldExpiresAt = booking.HoldExpiresAt,
                VenueId = booking.Court.VenueId,
                VenueName = booking.Court.Venue.VenueName,
                Address = booking.Court.Venue.Address,
                CourtId = booking.CourtId,
                CourtNumber = booking.Court.CourtNumber,
                StartTime = booking.StartTime,
                EndTime = booking.EndTime,
                DurationHours = booking.Slots.Any()
                    ? booking.Slots.Sum(slot => EF.Functions.DateDiffMinute(slot.StartTime, slot.EndTime)) / 60d
                    : EF.Functions.DateDiffMinute(booking.StartTime, booking.EndTime) / 60d,
                HourlyPrice = booking.HourlyPriceSnapshot,
                CourtAmount = booking.CourtAmount,
                TotalAmount = booking.TotalAmount,
                PaymentStatus = booking.Payments.OrderByDescending(payment => payment.PaymentId)
                    .Select(payment => payment.Status).FirstOrDefault() ?? "Pending",
                CheckInStatus = booking.Status == "Cancelled" || booking.Status == "Expired"
                    ? "NotApplicable"
                    : booking.Operation != null && (booking.Operation.CheckInStatus == "CheckedIn" || booking.Operation.CheckInStatus == "NoShow")
                        ? booking.Operation.CheckInStatus
                        : booking.Status != "Confirmed" && booking.Status != "Completed"
                            ? "NotOpen"
                            : localNow < booking.StartTime.AddMinutes(-30)
                                ? "NotOpen"
                                : localNow <= booking.EndTime ? "Ready" : "Missed",
                CheckedInAt = booking.Operation == null ? null : booking.Operation.CheckedInAt,
                CheckInCode = (booking.Status == "Confirmed" || booking.Status == "Completed")
                    && !booking.CheckInGroups.Any()
                    && localNow >= booking.StartTime.AddMinutes(-30)
                    && localNow <= booking.EndTime
                    ? booking.BookingCode
                    : null,
                CanCancel = (booking.Status == "Holding" || booking.Status == "Confirmed")
                    && !booking.Payments.Any(item => item.Status == "Paid")
                    && localNow < booking.StartTime
                    && (booking.Operation == null || booking.Operation.CheckInStatus != "CheckedIn"),
                CanRetryPayment = booking.Status == "Holding"
                    && booking.HoldExpiresAt > utcNow
                    && booking.Payments.OrderByDescending(payment => payment.PaymentId)
                        .Select(payment => payment.Status).FirstOrDefault() == "Pending"
                    && booking.Payments.OrderByDescending(payment => payment.PaymentId)
                        .Select(payment => payment.RejectionReason).FirstOrDefault() != null,
                HasReviewed = booking.Ratings.Any(),
                CanReview = (booking.Status == "Completed" || (booking.Operation != null && booking.Operation.CheckInStatus == "CheckedIn"))
                    && !booking.Ratings.Any(),
                Slots = booking.Slots.OrderBy(slot => slot.StartTime).ThenBy(slot => slot.CourtId).Select(slot => new BookingSlotResponse
                {
                    BookingSlotId = slot.BookingSlotId,
                    CourtId = slot.CourtId,
                    CourtNumber = slot.Court.CourtNumber,
                    CheckInGroupId = slot.CheckInGroupId,
                    StartTime = slot.StartTime,
                    EndTime = slot.EndTime,
                    HourlyPrice = slot.HourlyPriceSnapshot,
                    CourtAmount = slot.CourtAmount
                }).ToList(),
                CheckInGroups = booking.CheckInGroups.OrderBy(group => group.StartTime).ThenBy(group => group.CourtId).Select(group => new BookingCheckInGroupResponse
                {
                    BookingCheckInGroupId = group.BookingCheckInGroupId,
                    CourtId = group.CourtId,
                    CourtNumber = group.Court.CourtNumber,
                    StartTime = group.StartTime,
                    EndTime = group.EndTime,
                    CheckInCode = (booking.Status == "Confirmed" || booking.Status == "Completed")
                        && group.CheckInStatus == "Ready"
                        && localNow >= group.StartTime.AddMinutes(-30)
                        && localNow <= group.EndTime
                            ? group.CheckInCode
                            : null,
                    CheckInStatus = group.CheckInStatus,
                    CheckedInAt = group.CheckedInAt
                }).ToList()
            })
            .ToListAsync(cancellationToken);

        foreach (var booking in bookings)
        {
            if (string.IsNullOrWhiteSpace(booking.BookingCode)) booking.BookingCode = $"PL-{booking.BookingId}";
            booking.CheckInStatus = BookingOccurrencePolicy.GetCheckInStatus(
                booking.Status,
                booking.CheckInStatus,
                booking.CheckInGroups.Select(group => new BookingOccurrence(group.StartTime, group.EndTime, group.CheckInStatus)),
                localNow,
                booking.StartTime,
                booking.EndTime,
                inactiveStatus: "NotApplicable",
                overdueStatus: "Missed");
            booking.CreatedAt = AsUtc(booking.CreatedAt);
            booking.HoldExpiresAt = AsUtc(booking.HoldExpiresAt);
            booking.CheckedInAt = AsUtc(booking.CheckedInAt);
            foreach (var group in booking.CheckInGroups) group.CheckedInAt = AsUtc(group.CheckedInAt);
        }
        return Ok(Pagination.Create(bookings, totalCount, page, pageSize));
    }
    public async Task<ServiceResult<BookingHoldingResponse>> GetBooking(int bookingId, CancellationToken cancellationToken)
    {
        var booking = await LoadOwnedBookingReadAsync(bookingId, cancellationToken);
        return booking is null ? NotFound(new { message = "Không tìm thấy booking." }) : Ok(MapBooking(booking, booking.Court));
    }
    public async Task<ServiceResult<BookingHoldingGroupResponse>> GetHoldingGroup(Guid paymentGroupId, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        var bookings = await _dbContext.Bookings.AsNoTracking()
            .AsSplitQuery()
            .Include(item => item.Court).ThenInclude(item => item.Venue)
            .Include(item => item.Payments).ThenInclude(item => item.StatusHistories)
            .Include(item => item.StatusHistories)
            .Include(item => item.Operation)
            .Include(item => item.Ratings)
            .Where(item => item.Player!.UserId == userId.Value && item.Payments.Any(payment => payment.PaymentGroupId == paymentGroupId))
            .OrderBy(item => item.StartTime)
            .ThenBy(item => item.CourtId)
            .ToListAsync(cancellationToken);
        if (bookings.Count == 0) return NotFound(new { message = "Payment group not found." });

        return Ok(new BookingHoldingGroupResponse
        {
            PaymentGroupId = paymentGroupId,
            TotalAmount = bookings.SelectMany(item => item.Payments).Where(item => item.PaymentGroupId == paymentGroupId).Sum(item => item.Amount),
            Bookings = bookings.Select(item => MapBooking(item, item.Court)).ToList()
        });
    }
    public async Task<ServiceResult<BookingHoldingResponse>> CompletePayment(
        int bookingId,
        CompleteBookingPaymentRequest request,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_dbContext, transaction, $"booking-payment:{bookingId}", cancellationToken))
            return Conflict(new { message = "Booking đang được xử lý. Vui lòng thử lại." });

        var booking = await LoadOwnedBookingAsync(bookingId, cancellationToken);
        if (booking is null) return NotFound(new { message = "Không tìm thấy booking." });
        if (booking.Status == "Confirmed") return Ok(MapBooking(booking, booking.Court));
        if (booking.Status != "Holding") return Conflict(new { message = $"Booking đang ở trạng thái {booking.Status}." });
        if (booking.HoldExpiresAt <= DateTime.UtcNow)
        {
            await ExpireHoldingAsync(booking, "Hết thời gian trước khi thanh toán", cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            PublishBookingChanged(booking, "Expired", "Deleted");
            return Conflict(new { message = "Thời gian giữ chỗ đã hết. Slot đã được mở lại." });
        }

        if (request.PaymentMethod == "BankTransfer")
            return Conflict(new { message = "Chuyển khoản ngân hàng cần gửi biên lai và chờ chủ sân xác nhận." });

        var previous = booking.Status;
        booking.Status = "Confirmed";
        booking.HoldExpiresAt = null;
        var payment = booking.Payments.OrderByDescending(item => item.PaymentId).First();
        var previousPaymentStatus = payment.Status;
        payment.PaymentMethod = request.PaymentMethod;
        if (request.PaymentMethod == "AtCourt")
        {
            payment.Status = "Pending";
            payment.PaidAt = null;
            payment.StatusHistories.Add(NewPaymentHistory(previousPaymentStatus, "Pending", "AtCourtSelected", "Khách chọn thanh toán tại sân", userId));
            booking.StatusHistories.Add(NewHistory(previous, "Confirmed", "Giữ sân - chờ thanh toán tại quầy", userId));
        }
        else
        {
            payment.Status = "Paid";
            payment.PaidAt = DateTime.UtcNow;
            payment.StatusHistories.Add(NewPaymentHistory(previousPaymentStatus, "Paid", "LegacyPaymentCompleted", $"Thanh toán {request.PaymentMethod}", userId));
            booking.StatusHistories.Add(NewHistory(previous, "Confirmed", $"Thanh toán {request.PaymentMethod} thành công", userId));
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        PublishBookingChanged(booking, "Confirmed", "Updated");
        return Ok(MapBooking(booking, booking.Court));
    }
    public async Task<ServiceResult> CancelHolding(int bookingId, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_dbContext, transaction, $"booking-payment:{bookingId}", cancellationToken))
            return Conflict(new { message = "Booking đang được xử lý." });
        var booking = await LoadOwnedBookingAsync(bookingId, cancellationToken);
        if (booking is null) return NotFound(new { message = "Không tìm thấy booking." });
        if (booking.Status != "Holding") return Conflict(new { message = "Chỉ có thể hủy booking đang giữ chỗ." });
        booking.Status = "Cancelled";
        booking.HoldExpiresAt = null;
        foreach (var payment in booking.Payments.Where(item => item.Status is "Pending" or "WaitingForConfirmation"))
        {
            var fromPaymentStatus = payment.Status;
            payment.Status = "Cancelled";
            payment.StatusHistories.Add(NewPaymentHistory(fromPaymentStatus, "Cancelled", "BookingCancelled", "Player hủy giữ chỗ", userId));
        }
        booking.StatusHistories.Add(NewHistory("Holding", "Cancelled", "Player hủy giữ chỗ", userId));
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        PublishBookingChanged(booking, "Cancelled", "Deleted");
        return NoContent();
    }
    public async Task<ServiceResult> CancelBooking(
        int bookingId,
        CancelPlayerBookingRequest request,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_dbContext, transaction, $"booking-payment:{bookingId}", cancellationToken))
            return Conflict(new { message = "Booking đang được xử lý." });

        var booking = await LoadOwnedBookingAsync(bookingId, cancellationToken);
        if (booking is null) return NotFound(new { message = "Không tìm thấy booking." });
        if (booking.Status is "Cancelled" or "Expired") return NoContent();
        if (booking.Status is not ("Holding" or "Confirmed"))
            return Conflict(new { message = $"Không thể hủy booking ở trạng thái {booking.Status}." });
        if (booking.Payments.Any(item => item.Status == "Paid"))
            return Conflict(new { message = "\u0110\u01a1n \u0111\u00e3 thanh to\u00e1n kh\u00f4ng th\u1ec3 h\u1ee7y." });
        if (VietnamTime.Now >= booking.StartTime)
            return Conflict(new { message = "Không thể hủy booking đã đến giờ chơi." });
        if (booking.Operation?.CheckInStatus == "CheckedIn")
            return Conflict(new { message = "Booking đã check-in nên không thể hủy." });

        var cancellationReason = request.Reason.Trim();
        var previous = booking.Status;
        booking.Status = "Cancelled";
        booking.HoldExpiresAt = null;
        foreach (var payment in booking.Payments.Where(item => item.Status is "Pending" or "WaitingForConfirmation"))
        {
            var fromPaymentStatus = payment.Status;
            payment.Status = "Cancelled";
            payment.StatusHistories.Add(NewPaymentHistory(fromPaymentStatus, "Cancelled", "BookingCancelled", $"Player hủy booking: {cancellationReason}", userId));
        }
        booking.StatusHistories.Add(NewHistory(previous, "Cancelled", $"Player hủy booking: {cancellationReason}", userId));
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        PublishBookingChanged(booking, "Cancelled", "Deleted");
        return NoContent();
    }
    public async Task<ServiceResult<BookingHoldingResponse>> RetryPayment(int bookingId, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_dbContext, transaction, $"booking-payment:{bookingId}", cancellationToken))
            return Conflict(new { message = "Booking đang được xử lý." });

        var booking = await LoadOwnedBookingAsync(bookingId, cancellationToken);
        if (booking is null) return NotFound(new { message = "Không tìm thấy booking." });
        if (booking.Status != "Holding" || booking.HoldExpiresAt <= DateTime.UtcNow)
            return Conflict(new { message = "Booking không còn trong thời gian giữ chỗ để thanh toán lại." });

        var payment = booking.Payments.OrderByDescending(item => item.PaymentId).FirstOrDefault();
        if (payment is null || payment.Status != "Pending")
            return Conflict(new { message = "Thanh toán chưa ở trạng thái cho phép thử lại." });

        var bankAccount = await _dbContext.OwnerBankAccounts.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OwnerId == booking.Court.Venue.OwnerId && item.IsActive, cancellationToken);
        payment.PaymentMethod = "BankTransfer";
        payment.ReceiptImageUrl = null;
        payment.SubmittedAt = null;
        payment.VerifiedAt = null;
        payment.VerifiedByUserId = null;
        payment.RejectionReason = null;
        payment.BankCode = bankAccount?.BankCode;
        payment.BankName = bankAccount?.BankName;
        payment.BankAccountNumber = bankAccount?.AccountNumber;
        payment.BankAccountName = bankAccount?.AccountHolderName;
        payment.QrImageUrl = bankAccount is null ? null : BuildVietQrUrl(bankAccount, payment.Amount, payment.TransferContent ?? booking.BookingCode!);
        payment.StatusHistories.Add(NewPaymentHistory("Pending", "Pending", "RetryRequested", "Player yêu cầu thanh toán lại", userId));
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok(MapBooking(booking, booking.Court));
    }

    private async Task<Booking?> LoadOwnedBookingAsync(int bookingId, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        return await _dbContext.Bookings
            .AsSplitQuery()
            .Include(booking => booking.Court).ThenInclude(court => court.Venue)
            .Include(booking => booking.Player)
            .Include(booking => booking.Payments).ThenInclude(payment => payment.StatusHistories)
            .Include(booking => booking.StatusHistories)
            .Include(booking => booking.Operation)
            .Include(booking => booking.Ratings)
            .Include(booking => booking.Slots).ThenInclude(slot => slot.Court)
            .Include(booking => booking.CheckInGroups).ThenInclude(group => group.Court)
            .SingleOrDefaultAsync(booking => booking.BookingId == bookingId && booking.Player!.UserId == userId, cancellationToken);
    }

    private async Task<Booking?> LoadOwnedBookingReadAsync(int bookingId, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        return await _dbContext.Bookings
            .AsNoTracking()
            .AsSplitQuery()
            .Include(booking => booking.Court).ThenInclude(court => court.Venue)
            .Include(booking => booking.Payments).ThenInclude(payment => payment.StatusHistories)
            .Include(booking => booking.StatusHistories)
            .Include(booking => booking.Operation)
            .Include(booking => booking.Ratings)
            .Include(booking => booking.Slots).ThenInclude(slot => slot.Court)
            .Include(booking => booking.CheckInGroups).ThenInclude(group => group.Court)
            .SingleOrDefaultAsync(booking => booking.BookingId == bookingId && booking.Player!.UserId == userId, cancellationToken);
    }

    private async Task<Player?> GetOrCreatePlayerAsync(int userId, CancellationToken cancellationToken)
    {
        var player = await _dbContext.Players
            .Where(item => item.UserId == userId)
            .OrderByDescending(item => item.Prestige)
            .ThenByDescending(item => item.SkillLevel)
            .ThenByDescending(item => item.PlayerId)
            .FirstOrDefaultAsync(cancellationToken);
        if (player is not null) return player;
        var user = await _dbContext.Users.SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (user is null || !(user.UserType.Equals("Player", StringComparison.OrdinalIgnoreCase) || user.UserType.Equals("User", StringComparison.OrdinalIgnoreCase))) return null;
        player = new Player { UserId = userId, Prestige = 0, SkillLevel = 0 };
        _dbContext.Players.Add(player);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return player;
    }

    private Task ExpireHoldingAsync(Booking booking, string reason, CancellationToken cancellationToken)
    {
        var previous = booking.Status;
        booking.Status = "Expired";
        booking.HoldExpiresAt = null;
        foreach (var payment in booking.Payments.Where(item => item.Status is "Pending" or "WaitingForConfirmation"))
        {
            var fromStatus = payment.Status;
            payment.Status = "Expired";
            payment.StatusHistories.Add(NewPaymentHistory(fromStatus, "Expired", "BookingExpired", reason, null));
        }
        booking.StatusHistories.Add(NewHistory(previous, "Expired", reason, null));
        return Task.CompletedTask;
    }

    private static BookingStatusHistory NewHistory(string? from, string to, string reason, int? actorUserId) => new()
    {
        FromStatus = from,
        ToStatus = to,
        Reason = reason,
        ActorUserId = actorUserId,
        ChangedAt = DateTime.UtcNow
    };

    private static BookingHoldingResponse MapBooking(Booking booking, Court court) => new()
    {
        BookingId = booking.BookingId,
        BookingCode = booking.BookingCode ?? $"PL-{booking.BookingId}",
        Status = booking.Status,
        CreatedAt = AsUtc(booking.CreatedAt),
        HoldExpiresAt = AsUtc(booking.HoldExpiresAt),
        VenueId = court.VenueId,
        VenueName = court.Venue.VenueName,
        Address = court.Venue.Address,
        CourtId = court.CourtId,
        CourtNumber = court.CourtNumber,
        StartTime = booking.StartTime,
        EndTime = booking.EndTime,
        DurationHours = booking.Slots.Count != 0
            ? booking.Slots.Sum(slot => (slot.EndTime - slot.StartTime).TotalHours)
            : (booking.EndTime - booking.StartTime).TotalHours,
        HourlyPrice = booking.HourlyPriceSnapshot,
        CourtAmount = booking.CourtAmount,
        TotalAmount = booking.TotalAmount,
        PaymentStatus = booking.Payments.OrderByDescending(item => item.PaymentId).Select(item => item.Status).FirstOrDefault() ?? "Pending",
        CheckInStatus = GetCheckInStatus(booking),
        CheckedInAt = AsUtc(booking.Operation?.CheckedInAt),
        CheckInCode = (booking.Status is "Confirmed" or "Completed")
            && booking.CheckInGroups.Count == 0
            && VietnamTime.Now >= booking.StartTime.AddMinutes(-30)
            && VietnamTime.Now <= booking.EndTime
                ? booking.BookingCode
                : null,
        CanCancel = booking.Status is "Holding" or "Confirmed"
            && !booking.Payments.Any(item => item.Status == "Paid")
            && VietnamTime.Now < booking.StartTime
            && booking.Operation?.CheckInStatus != "CheckedIn",
        CanRetryPayment = booking.Status == "Holding"
            && booking.HoldExpiresAt > DateTime.UtcNow
            && booking.Payments.OrderByDescending(item => item.PaymentId).FirstOrDefault()?.Status == "Pending"
            && !string.IsNullOrWhiteSpace(booking.Payments.OrderByDescending(item => item.PaymentId).FirstOrDefault()?.RejectionReason),
        HasReviewed = booking.Ratings.Any(item => item.BookingId == booking.BookingId),
        CanReview = (booking.Status == "Completed" || booking.Operation?.CheckInStatus == "CheckedIn")
            && !booking.Ratings.Any(item => item.BookingId == booking.BookingId),
        BankTransfer = booking.Payments.OrderByDescending(item => item.PaymentId).Select(MapTransfer).FirstOrDefault(),
        StatusHistory = booking.StatusHistories.OrderBy(item => item.ChangedAt).Select(item => new BookingStatusHistoryResponse { FromStatus = item.FromStatus, ToStatus = item.ToStatus, Reason = item.Reason, ChangedAt = AsUtc(item.ChangedAt) }).ToList(),
        Slots = booking.Slots.OrderBy(item => item.StartTime).ThenBy(item => item.CourtId).Select(item => new BookingSlotResponse
        {
            BookingSlotId = item.BookingSlotId,
            CourtId = item.CourtId,
            CourtNumber = item.Court.CourtNumber,
            CheckInGroupId = item.CheckInGroupId,
            StartTime = item.StartTime,
            EndTime = item.EndTime,
            HourlyPrice = item.HourlyPriceSnapshot,
            CourtAmount = item.CourtAmount
        }).ToList(),
        CheckInGroups = booking.CheckInGroups.OrderBy(item => item.StartTime).ThenBy(item => item.CourtId).Select(item => new BookingCheckInGroupResponse
        {
            BookingCheckInGroupId = item.BookingCheckInGroupId,
            CourtId = item.CourtId,
            CourtNumber = item.Court.CourtNumber,
            StartTime = item.StartTime,
            EndTime = item.EndTime,
            CheckInCode = (booking.Status is "Confirmed" or "Completed")
                && item.CheckInStatus == "Ready"
                && VietnamTime.Now >= item.StartTime.AddMinutes(-30)
                && VietnamTime.Now <= item.EndTime
                    ? item.CheckInCode
                    : null,
            CheckInStatus = item.CheckInStatus,
            CheckedInAt = AsUtc(item.CheckedInAt)
        }).ToList()
    };

    public void SetCurrentUserId(int? userId) => _currentUserId = userId;

    private int? _currentUserId;

    private int? CurrentUserId() => _currentUserId;

    private static string GetCheckInStatus(Booking booking)
    {
        return BookingOccurrencePolicy.GetCheckInStatus(
            booking.Status,
            booking.Operation?.CheckInStatus,
            booking.CheckInGroups.Select(group => new BookingOccurrence(group.StartTime, group.EndTime, group.CheckInStatus)),
            VietnamTime.Now,
            booking.StartTime,
            booking.EndTime,
            inactiveStatus: "NotApplicable",
            overdueStatus: "Missed");
    }
    private void PublishBookingChanged(Booking booking, string status, string action)
    {
        if (booking.Slots.Count > 0)
        {
            foreach (var slot in booking.Slots)
                _scheduleRealtime.Publish(new ScheduleChangedEvent(booking.Court.VenueId, slot.CourtId, slot.StartTime, slot.EndTime, status, action));
            return;
        }

        _scheduleRealtime.Publish(new ScheduleChangedEvent(booking.Court.VenueId, booking.CourtId, booking.StartTime, booking.EndTime, status, action));
    }
    private static DateTime AsUtc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static DateTime? AsUtc(DateTime? value) => value.HasValue ? AsUtc(value.Value) : null;
    private static BankTransferResponse MapTransfer(Payment payment) => new()
    {
        PaymentId = payment.PaymentId,
        BookingId = payment.BookingId,
        PaymentStatus = payment.Status,
        Amount = payment.Amount,
        TransferCode = payment.TransferCode,
        TransferContent = payment.TransferContent,
        BankCode = payment.BankCode,
        BankName = payment.BankName,
        BankAccountNumber = payment.BankAccountNumber,
        BankAccountName = payment.BankAccountName,
        QrImageUrl = payment.QrImageUrl,
        ReceiptImageUrl = payment.ReceiptImageUrl,
        SubmittedAt = payment.SubmittedAt,
        VerifiedAt = payment.VerifiedAt,
        RejectionReason = payment.RejectionReason,
        History = payment.StatusHistories.OrderBy(item => item.CreatedAt).Select(item => new PaymentHistoryResponse
        {
            FromStatus = item.FromStatus,
            ToStatus = item.ToStatus,
            Action = item.Action,
            Reason = item.Reason,
            CreatedAt = item.CreatedAt
        }).ToList()
    };
    private static PaymentStatusHistory NewPaymentHistory(string? from, string to, string action, string? reason, int? actorUserId) => new()
    {
        FromStatus = from, ToStatus = to, Action = action, Reason = reason, ActorUserId = actorUserId, CreatedAt = DateTime.UtcNow
    };
    private static string BuildVietQrUrl(OwnerBankAccount account, decimal amount, string content)
    {
        var query = $"amount={Math.Round(amount):0}&addInfo={Uri.EscapeDataString(content)}&accountName={Uri.EscapeDataString(account.AccountHolderName)}";
        return $"https://img.vietqr.io/image/{Uri.EscapeDataString(account.BankCode)}-{Uri.EscapeDataString(account.AccountNumber)}-compact2.png?{query}";
    }
    private static decimal GetBasePrice(Venue venue) => decimal.TryParse(venue.BookingRules.FirstOrDefault(rule => rule.RuleType == "BasePrice")?.RuleContent, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : 0;
    private static decimal RoundMoney(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero);
}
