using System.Data;
using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;

namespace PicklinkBackend.Services;
public enum PlayerBookingServiceResultStatus
{
    Success,
    NoContent,
    BadRequest,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    StatusCode
}

public sealed record PlayerBookingServiceResult(
    PlayerBookingServiceResultStatus Status,
    object? Value = null,
    object? Error = null,
    int? RawStatusCode = null);

public sealed record PlayerBookingServiceResult<T>(
    PlayerBookingServiceResultStatus Status,
    T? Value = default,
    object? Error = null,
    int? RawStatusCode = null)
{
    public static implicit operator PlayerBookingServiceResult<T>(PlayerBookingServiceResult result) =>
        new(
            result.Status,
            result.Value is T value ? value : default,
            result.Error,
            result.RawStatusCode);
}

public sealed record PlayerBookingServiceDependencies(ApplicationDbContext DbContext, IConfiguration Configuration, ScheduleRealtimeNotifier ScheduleRealtime, PlayerScheduleConflictService PlayerScheduleConflict);

public class PlayerBookingService
{
    private static readonly string[] InactiveStatuses = ["Cancelled", "Expired"];
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
    private static PlayerBookingServiceResult Ok(object? value = null) =>
        new(PlayerBookingServiceResultStatus.Success, value);

    private static PlayerBookingServiceResult NoContent() =>
        new(PlayerBookingServiceResultStatus.NoContent);

    private static PlayerBookingServiceResult BadRequest(object? error = null) =>
        new(PlayerBookingServiceResultStatus.BadRequest, Error: error);

    private static PlayerBookingServiceResult Unauthorized(object? error = null) =>
        new(PlayerBookingServiceResultStatus.Unauthorized, Error: error);

    private static PlayerBookingServiceResult Forbid(object? error = null) =>
        new(PlayerBookingServiceResultStatus.Forbidden, Error: error);

    private static PlayerBookingServiceResult NotFound(object? error = null) =>
        new(PlayerBookingServiceResultStatus.NotFound, Error: error);

    private static PlayerBookingServiceResult Conflict(object? error = null) =>
        new(PlayerBookingServiceResultStatus.Conflict, Error: error);

    private static PlayerBookingServiceResult StatusCode(int statusCode, object? body = null) =>
        statusCode >= 400
            ? new(PlayerBookingServiceResultStatus.StatusCode, Error: body, RawStatusCode: statusCode)
            : new(PlayerBookingServiceResultStatus.StatusCode, Value: body, RawStatusCode: statusCode);
    public async Task<PlayerBookingServiceResult<PaginatedResponse<PlayerVenueSummaryResponse>>> GetVenues(
        string? search,
        string? area,
        double? minPrice,
        double? maxPrice,
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

        var now = DateTime.UtcNow;
        var keyword = search?.Trim();
        var normalizedArea = area?.Trim();
        var venueQuery = _dbContext.Venues.AsNoTracking()
            .Where(venue => venue.ApprovalStatus == "Approved"
                && venue.IsOpen
                && venue.Courts.Any(court => court.AvailabilityStatus == "Available"))
            .Where(HasActiveListingFee(now));
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
            var basePrice = double.TryParse(venue.BasePriceText, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : 0;
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
    public Task<PlayerBookingServiceResult<PaginatedResponse<PlayerVenueSummaryResponse>>> GetFavoriteVenues(
        int page = 1,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default) =>
        GetVenues(null, null, null, null, true, page, pageSize, cancellationToken);
    public async Task<PlayerBookingServiceResult> AddFavoriteVenue(int venueId, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var player = await GetOrCreatePlayerAsync(userId.Value, cancellationToken);
        if (player is null) return Forbid();
        var now = DateTime.UtcNow;
        if (!await _dbContext.Venues
            .Where(HasActiveListingFee(now))
            .AnyAsync(
                item => item.VenueId == venueId
                    && item.ApprovalStatus == "Approved"
                    && item.IsOpen,
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
    public async Task<PlayerBookingServiceResult> RemoveFavoriteVenue(int venueId, CancellationToken cancellationToken)
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
    public async Task<PlayerBookingServiceResult<PlayerCourtAvailabilityResponse>> GetAvailability(
        int venueId,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var venue = await _dbContext.Venues.AsNoTracking()
            .Include(item => item.Courts)
            .Include(item => item.BookingRules)
            .Where(HasActiveListingFee(now))
            .SingleOrDefaultAsync(
                venue => venue.VenueId == venueId
                    && venue.ApprovalStatus == "Approved"
                    && venue.IsOpen,
                cancellationToken);
        if (venue is null) return NotFound(new { message = "Không tìm thấy cụm sân." });

        var dayStart = date.ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);
        var currentUserId = CurrentUserId();
        var bookings = await _dbContext.Bookings.AsNoTracking()
            .Where(booking => booking.Court.VenueId == venueId && booking.StartTime < dayEnd && booking.EndTime > dayStart &&
                !InactiveStatuses.Contains(booking.Status) && (booking.Status != "Holding" || booking.HoldExpiresAt > now))
            .Include(booking => booking.Player)
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
                var overlap = bookings.FirstOrDefault(booking => booking.CourtId == court.CourtId && booking.StartTime < end && booking.EndTime > start);
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
    public async Task<PlayerBookingServiceResult<BookingHoldingResponse>> CreateHolding(
        CreateBookingHoldRequest request,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var player = await GetOrCreatePlayerAsync(userId.Value, cancellationToken);
        if (player is null) return BadRequest(new { message = "Tài khoản chưa có hồ sơ người chơi." });

        var selectedSlots = request.SlotStarts.Distinct().OrderBy(item => item).ToList();
        if (selectedSlots.Count != request.SlotStarts.Count)
            return BadRequest(new { message = "Danh sách slot bị trùng." });
        for (var index = 1; index < selectedSlots.Count; index++)
            if (selectedSlots[index].ToTimeSpan() - selectedSlots[index - 1].ToTimeSpan() != TimeSpan.FromMinutes(30))
                return BadRequest(new { message = "Chỉ được chọn các slot 30 phút liên tiếp." });
        if (selectedSlots.Any(slot => slot.Minute % 30 != 0 || slot.Second != 0))
            return BadRequest(new { message = "Slot phải bắt đầu tại phút 00 hoặc 30." });

        var startTime = request.Date.ToDateTime(selectedSlots[0]);
        var endTime = request.Date.ToDateTime(selectedSlots[^1]).AddMinutes(30);
        if (startTime <= DateTime.Now)
            return BadRequest(new { message = "Không thể giữ chỗ cho khung giờ đã qua." });

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_dbContext, transaction, $"court-booking:{request.CourtId}", cancellationToken))
            return Conflict(new { message = "Sân đang được người khác thao tác. Vui lòng thử lại." });
        if (!await SqlServerBookingLock.AcquireAsync(_dbContext, transaction, $"player-schedule:{player.PlayerId}", cancellationToken))
            return Conflict(new { message = "Lịch của bạn đang được cập nhật. Vui lòng thử lại." });

        var court = await _dbContext.Courts
            .Include(item => item.Venue).ThenInclude(venue => venue.BookingRules)
            .Include(item => item.Venue).ThenInclude(venue => venue.VenueListingPayments)
            .SingleOrDefaultAsync(item => item.CourtId == request.CourtId, cancellationToken);
        if (court is null) return NotFound(new { message = "Không tìm thấy sân con." });
        if (court.Venue.ApprovalStatus != "Approved"
            || !court.Venue.IsOpen
            || !court.Venue.VenueListingPayments.Any(payment => payment.Status == "Confirmed" && payment.PaidUntil >= DateTime.UtcNow)
            || court.AvailabilityStatus != "Available")
            return Conflict(new { message = "Sân hiện không nhận đặt chỗ." });

        var opening = request.Date.ToDateTime(court.Venue.OpenTime);
        var closing = request.Date.ToDateTime(court.Venue.CloseTime);
        if (startTime < opening || endTime > closing)
            return BadRequest(new { message = $"Khung giờ phải nằm trong giờ mở cửa {court.Venue.OpenTime:HH:mm}–{court.Venue.CloseTime:HH:mm}." });

        var utcNow = DateTime.UtcNow;
        var staleHoldings = await _dbContext.Bookings
            .Include(booking => booking.Payments).ThenInclude(payment => payment.StatusHistories)
            .Where(booking => booking.CourtId == request.CourtId && booking.Status == "Holding" && booking.HoldExpiresAt <= utcNow)
            .ToListAsync(cancellationToken);
        foreach (var stale in staleHoldings) await ExpireHoldingAsync(stale, "Hết thời gian giữ chỗ", cancellationToken);
        if (staleHoldings.Count > 0) await _dbContext.SaveChangesAsync(cancellationToken);

        if (await _playerScheduleConflict.HasConflictAsync(
                player.PlayerId,
                startTime,
                endTime,
                cancellationToken: cancellationToken))
            return Conflict(new { message = "Bạn đã có lịch đặt sân hoặc ghép trận trùng với khung giờ này." });

        var overlaps = await _dbContext.Bookings.AnyAsync(booking =>
            booking.CourtId == request.CourtId && !InactiveStatuses.Contains(booking.Status) &&
            (booking.Status != "Holding" || booking.HoldExpiresAt > utcNow) &&
            booking.StartTime < endTime && booking.EndTime > startTime,
            cancellationToken);
        if (overlaps) return Conflict(new { message = "Một hoặc nhiều slot vừa được người khác giữ. Hãy tải lại lịch." });

        var hourlyPrice = court.HourlyPrice > 0 ? court.HourlyPrice : GetBasePrice(court.Venue);
        var durationHours = (endTime - startTime).TotalHours;
        var courtAmount = RoundMoney(hourlyPrice * durationHours);
        var total = courtAmount;
        var holdMinutes = Math.Clamp(_configuration.GetValue("Booking:HoldingMinutes", 5), 1, 60);

        var booking = new Booking
        {
            PlayerId = player.PlayerId,
            CourtId = court.CourtId,
            StartTime = startTime,
            EndTime = endTime,
            Status = "Holding",
            BookingCode = $"PL-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
            CreatedAt = utcNow,
            HoldExpiresAt = utcNow.AddMinutes(holdMinutes),
            HourlyPriceSnapshot = hourlyPrice,
            CourtAmount = courtAmount,
            TotalAmount = total
        };
        booking.StatusHistories.Add(NewHistory(null, "Holding", "Player tạo giữ chỗ", userId));
        var bankAccount = await _dbContext.OwnerBankAccounts.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OwnerId == court.Venue.OwnerId && item.IsActive, cancellationToken);
        var transferContent = booking.BookingCode!;
        var payment = new Payment
        {
            PayerId = player.PlayerId,
            Amount = total,
            PaymentMethod = "BankTransfer",
            Status = "Pending",
            TransferCode = booking.BookingCode!.Replace("-", string.Empty),
            TransferContent = transferContent,
            BankCode = bankAccount?.BankCode,
            BankName = bankAccount?.BankName,
            BankAccountNumber = bankAccount?.AccountNumber,
            BankAccountName = bankAccount?.AccountHolderName,
            QrImageUrl = bankAccount is null ? null : BuildVietQrUrl(bankAccount, total, transferContent)
        };
        payment.StatusHistories.Add(NewPaymentHistory(null, "Pending", "Created", "Tạo yêu cầu chuyển khoản", userId));
        booking.Payments.Add(payment);
        _dbContext.Bookings.Add(booking);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _scheduleRealtime.Publish(new ScheduleChangedEvent(court.VenueId, court.CourtId, booking.StartTime, booking.EndTime, "Holding", "Created"));

        return Ok(MapBooking(booking, court));
    }
    public async Task<PlayerBookingServiceResult<PaginatedResponse<BookingHoldingResponse>>> GetMyBookings(
        int page = 1,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();

        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);
        var query = _dbContext.Bookings.AsNoTracking()
            .Where(booking => booking.Player != null && booking.Player.UserId == userId);
        var totalCount = await query.CountAsync(cancellationToken);
        var localNow = DateTime.Now;
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
                DurationHours = EF.Functions.DateDiffMinute(booking.StartTime, booking.EndTime) / 60d,
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
                CheckInCode = booking.Status == "Confirmed" || booking.Status == "Completed"
                    ? booking.BookingCode
                    : null,
                CanCancel = (booking.Status == "Holding" || booking.Status == "Confirmed")
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
                    && !booking.Ratings.Any()
            })
            .ToListAsync(cancellationToken);

        foreach (var booking in bookings)
        {
            if (string.IsNullOrWhiteSpace(booking.BookingCode)) booking.BookingCode = $"PL-{booking.BookingId}";
            booking.CreatedAt = AsUtc(booking.CreatedAt);
            booking.HoldExpiresAt = AsUtc(booking.HoldExpiresAt);
            booking.CheckedInAt = AsUtc(booking.CheckedInAt);
        }
        return Ok(Pagination.Create(bookings, totalCount, page, pageSize));
    }
    public async Task<PlayerBookingServiceResult<BookingHoldingResponse>> GetBooking(int bookingId, CancellationToken cancellationToken)
    {
        var booking = await LoadOwnedBookingReadAsync(bookingId, cancellationToken);
        return booking is null ? NotFound(new { message = "Không tìm thấy booking." }) : Ok(MapBooking(booking, booking.Court));
    }
    public async Task<PlayerBookingServiceResult<BookingHoldingResponse>> CompletePayment(
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
    public async Task<PlayerBookingServiceResult> CancelHolding(int bookingId, CancellationToken cancellationToken)
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
    public async Task<PlayerBookingServiceResult> CancelBooking(
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
        if (DateTime.Now >= booking.StartTime)
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
    public async Task<PlayerBookingServiceResult<BookingHoldingResponse>> RetryPayment(int bookingId, CancellationToken cancellationToken)
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
        DurationHours = (booking.EndTime - booking.StartTime).TotalHours,
        HourlyPrice = booking.HourlyPriceSnapshot,
        CourtAmount = booking.CourtAmount,
        TotalAmount = booking.TotalAmount,
        PaymentStatus = booking.Payments.OrderByDescending(item => item.PaymentId).Select(item => item.Status).FirstOrDefault() ?? "Pending",
        CheckInStatus = GetCheckInStatus(booking),
        CheckedInAt = AsUtc(booking.Operation?.CheckedInAt),
        CheckInCode = booking.Status is "Confirmed" or "Completed" ? booking.BookingCode : null,
        CanCancel = booking.Status is "Holding" or "Confirmed"
            && DateTime.Now < booking.StartTime
            && booking.Operation?.CheckInStatus != "CheckedIn",
        CanRetryPayment = booking.Status == "Holding"
            && booking.HoldExpiresAt > DateTime.UtcNow
            && booking.Payments.OrderByDescending(item => item.PaymentId).FirstOrDefault()?.Status == "Pending"
            && !string.IsNullOrWhiteSpace(booking.Payments.OrderByDescending(item => item.PaymentId).FirstOrDefault()?.RejectionReason),
        HasReviewed = booking.Ratings.Any(item => item.BookingId == booking.BookingId),
        CanReview = (booking.Status == "Completed" || booking.Operation?.CheckInStatus == "CheckedIn")
            && !booking.Ratings.Any(item => item.BookingId == booking.BookingId),
        BankTransfer = booking.Payments.OrderByDescending(item => item.PaymentId).Select(MapTransfer).FirstOrDefault(),
        StatusHistory = booking.StatusHistories.OrderBy(item => item.ChangedAt).Select(item => new BookingStatusHistoryResponse { FromStatus = item.FromStatus, ToStatus = item.ToStatus, Reason = item.Reason, ChangedAt = AsUtc(item.ChangedAt) }).ToList()
    };

    public void SetCurrentUserId(int? userId) => _currentUserId = userId;

    private int? _currentUserId;

    private int? CurrentUserId() => _currentUserId;

    private static Expression<Func<Venue, bool>> HasActiveListingFee(DateTime now) =>
        venue => venue.VenueListingPayments.Any(payment =>
            payment.Status == "Confirmed"
            && payment.PaidUntil != null
            && payment.PaidUntil >= now);
    private static string GetCheckInStatus(Booking booking)
    {
        if (booking.Status is "Cancelled" or "Expired") return "NotApplicable";
        if (booking.Operation?.CheckInStatus is "CheckedIn" or "NoShow") return booking.Operation.CheckInStatus;
        if (booking.Status is not ("Confirmed" or "Completed")) return "NotOpen";
        var now = DateTime.Now;
        if (now < booking.StartTime.AddMinutes(-30)) return "NotOpen";
        if (now <= booking.EndTime) return "Ready";
        return "Missed";
    }
    private void PublishBookingChanged(Booking booking, string status, string action) =>
        _scheduleRealtime.Publish(new ScheduleChangedEvent(booking.Court.VenueId, booking.CourtId, booking.StartTime, booking.EndTime, status, action));
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
    private static string BuildVietQrUrl(OwnerBankAccount account, double amount, string content)
    {
        var query = $"amount={Math.Round(amount):0}&addInfo={Uri.EscapeDataString(content)}&accountName={Uri.EscapeDataString(account.AccountHolderName)}";
        return $"https://img.vietqr.io/image/{Uri.EscapeDataString(account.BankCode)}-{Uri.EscapeDataString(account.AccountNumber)}-compact2.png?{query}";
    }
    private static double GetBasePrice(Venue venue) => double.TryParse(venue.BookingRules.FirstOrDefault(rule => rule.RuleType == "BasePrice")?.RuleContent, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : 0;
    private static double RoundMoney(double value) => Math.Round(value, 0, MidpointRounding.AwayFromZero);
}
