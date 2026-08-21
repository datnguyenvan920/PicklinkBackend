using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Repositories;
using PicklinkBackend.Services.Bookings;
using PicklinkBackend.Services.Schedules;
using PicklinkBackend.Services.Shared;

namespace PicklinkBackend.Services.Bookings.Implementations;

public sealed record PlayerBookingServiceDependencies(
    IBookingRepository BookingRepository,
    IVenueRepository VenueRepository,
    IUserRepository UserRepository,
    IConfiguration Configuration,
    ScheduleRealtimeNotifier ScheduleRealtime,
    PlayerScheduleConflictService PlayerScheduleConflict);

public class PlayerBookingService : IPlayerBookingService
{
    private static readonly string[] InactiveStatuses = ["Cancelled", "Expired"];
    private const int MaximumAdvanceBookingMonths = 1;
    private readonly IBookingRepository _bookingRepository;
    private readonly IVenueRepository _venueRepository;
    private readonly IUserRepository _userRepository;
    private readonly IConfiguration _configuration;
    private readonly ScheduleRealtimeNotifier _scheduleRealtime;
    private readonly PlayerScheduleConflictService _playerScheduleConflict;

    private PlayerBookingService(
        IBookingRepository bookingRepository,
        IVenueRepository venueRepository,
        IUserRepository userRepository,
        IConfiguration configuration,
        ScheduleRealtimeNotifier scheduleRealtime,
        PlayerScheduleConflictService playerScheduleConflict)
    {
        _bookingRepository = bookingRepository;
        _venueRepository = venueRepository;
        _userRepository = userRepository;
        _configuration = configuration;
        _scheduleRealtime = scheduleRealtime;
        _playerScheduleConflict = playerScheduleConflict;
    }

    public PlayerBookingService(PlayerBookingServiceDependencies dependencies)
        : this(
            dependencies.BookingRepository,
            dependencies.VenueRepository,
            dependencies.UserRepository,
            dependencies.Configuration,
            dependencies.ScheduleRealtime,
            dependencies.PlayerScheduleConflict)
    {
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

    private static ServiceResult PhoneNumberRequired() => BadRequest(new
    {
        message = "Vui lòng cập nhật số điện thoại trong hồ sơ trước khi đặt sân hoặc thanh toán.",
        errorCode = ApiErrorCodes.PhoneNumberRequired
    });

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
            ? await _bookingRepository.GetFavoriteVenueIdsAsync(userId.Value, cancellationToken)
            : [];
        page = Pagination.NormalizePage(page);
        pageSize = Pagination.NormalizePageSize(pageSize);
        if (favoritesOnly && favoriteVenueIds.Count == 0)
            return Ok(Pagination.Create(Array.Empty<PlayerVenueSummaryResponse>(), 0, page, pageSize));

        var keyword = search?.Trim();
        var normalizedArea = area?.Trim();
        var venueQuery = _venueRepository.GetApprovedVenuesQueryable();
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

    public async Task<ServiceResult<List<PlayerVenueReviewResponse>>> GetVenueReviews(
        int venueId,
        CancellationToken cancellationToken)
    {
        if (!await _venueRepository.IsApprovedVenueAsync(venueId, cancellationToken))
            return NotFound(new { message = "Không tìm thấy cụm sân." });

        var rows = await _venueRepository.RatingHistories
            .AsNoTracking()
            .Where(review => review.TargetType == "Venue"
                && review.TargetId == venueId
                && !review.IsHidden
                && review.ModerationStatus == "Visible")
            .OrderByDescending(review => review.CreatedAt)
            .Select(review => new
            {
                review.RatingId,
                ReviewerName = review.IsAnonymous ? "Ẩn danh" : review.User.Username,
                CourtNumber = review.Booking == null ? (int?)null : review.Booking.Court.CourtNumber,
                review.Score,
                review.Comment,
                review.Tags,
                review.IsAnonymous,
                review.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(rows.Select(review => new PlayerVenueReviewResponse
        {
            RatingId = review.RatingId,
            ReviewerName = review.ReviewerName,
            CourtNumber = review.CourtNumber,
            Score = review.Score,
            Comment = review.Comment,
            Tags = SplitReviewTags(review.Tags),
            IsAnonymous = review.IsAnonymous,
            CreatedAt = review.CreatedAt
        }).ToList());
    }

    public async Task<ServiceResult> AddFavoriteVenue(int venueId, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var player = await GetOrCreatePlayerAsync(userId.Value, cancellationToken);
        if (player is null) return Forbid();
        if (!await _venueRepository.IsApprovedVenueAsync(venueId, cancellationToken))
            return NotFound(new { message = "Không tìm thấy cụm sân." });
        if (await _bookingRepository.GetFavoriteVenueAsync(player.PlayerId, venueId, cancellationToken) == null)
        {
            await _bookingRepository.AddFavoriteVenueAsync(new FavoriteVenue
            {
                PlayerId = player.PlayerId,
                VenueId = venueId,
                CreatedAt = DateTime.UtcNow
            }, cancellationToken);
            try
            {
                await _bookingRepository.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                if (await _bookingRepository.GetFavoriteVenueAsync(player.PlayerId, venueId, cancellationToken) == null)
                    throw;
            }
        }
        return NoContent();
    }

    public async Task<ServiceResult> RemoveFavoriteVenue(int venueId, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var player = await _bookingRepository.GetPlayerByUserIdAsync(userId.Value, cancellationToken);
        if (player != null)
        {
            var favorite = await _bookingRepository.GetFavoriteVenueAsync(player.PlayerId, venueId, cancellationToken);
            if (favorite is not null)
            {
                await _bookingRepository.RemoveFavoriteVenueAsync(favorite, cancellationToken);
                await _bookingRepository.SaveChangesAsync(cancellationToken);
            }
        }
        return NoContent();
    }

    public async Task<ServiceResult<PlayerCourtAvailabilityResponse>> GetAvailability(
        int venueId,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var venue = await _venueRepository.GetApprovedVenueForAvailabilityAsync(venueId, cancellationToken);
        if (venue is null) return NotFound(new { message = "Không tìm thấy cụm sân." });

        var dayStart = date.ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);
        var currentUserId = CurrentUserId();
        var now = DateTime.UtcNow;
        var bookings = await _bookingRepository.GetOverlappingBookingsAsync(venueId, dayStart, dayEnd, now, cancellationToken);

        var response = new PlayerCourtAvailabilityResponse
        {
            VenueId = venue.VenueId,
            VenueName = venue.VenueName,
            Address = venue.Address,
            PhoneNumber = venue.PhoneNumber,
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
                    MatchId = isOwnedHolding ? overlap!.MatchId : null,
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
        if (string.IsNullOrWhiteSpace(player.PhoneNumber)) return PhoneNumberRequired();

        var bookingDate = DateOnly.FromDateTime(VietnamTime.Now);
        var maxBookingDate = new DateOnly(bookingDate.Year, bookingDate.Month, 1)
            .AddMonths(MaximumAdvanceBookingMonths + 1)
            .AddDays(-1);
        if (request.Date < bookingDate || request.Date > maxBookingDate)
            return BadRequest(new { message = "Người chơi chỉ được đặt sân từ hôm nay đến hết tháng kế tiếp." });

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
            return BadRequest(new { message = "Người chơi chỉ được đặt sân từ hôm nay đến hết tháng kế tiếp." });
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
            return BadRequest(new { message = "Mỗi khung giờ chỉ được chọn một sân con." });
        var selectedCourtIds = selectedSlots.Select(item => item.CourtId).Distinct().ToList();
        if (selectedRanges.Any(slot => slot.Start <= VietnamTime.Now))
            return BadRequest(new { message = "Không thể giữ chỗ cho khung giờ đã qua." });

        await using var transaction = await _bookingRepository.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        if (!await SqlServerBookingLock.AcquireAsync(
                transaction,
                $"player-schedule:{player.PlayerId}",
                cancellationToken))
            return Conflict(new { message = "Lịch của bạn đang được xử lý. Vui lòng thử lại." });

        var courtScheduleLocks = selectedRanges
            .Select(slot => $"court-schedule:{slot.CourtId}:{slot.Start:yyyyMMdd}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(resource => resource, StringComparer.Ordinal)
            .ToList();
        foreach (var resource in courtScheduleLocks)
        {
            if (!await SqlServerBookingLock.AcquireAsync(transaction, resource, cancellationToken))
                return Conflict(new { message = "Lịch sân đang được xử lý. Vui lòng thử lại." });
        }

        var courts = await _venueRepository.GetCourtsByIdsAsync(selectedCourtIds, cancellationToken);
        if (courts.Count != selectedCourtIds.Count) return NotFound(new { message = "Không tìm thấy sân con." });
        if (courts.Select(item => item.VenueId).Distinct().Skip(1).Any())
            return BadRequest(new { message = "Các slot phải thuộc cùng một cụm sân." });
        var venue = await _venueRepository.GetApprovedVenueForAvailabilityAsync(courts[0].VenueId, cancellationToken);
        if (venue is null) return NotFound(new { message = "Không tìm thấy cụm sân." });
        var court = courts[0];
        if (!venue.IsOpen || courts.Any(c => c.AvailabilityStatus != "Available"))
            return Conflict(new { message = "Sân hiện không nhận đặt chỗ." });

        var courtsById = courts.ToDictionary(item => item.CourtId);
        if (selectedRanges.Any(slot => TimeOnly.FromDateTime(slot.Start) < venue.OpenTime
            || TimeOnly.FromDateTime(slot.End) > venue.CloseTime))
            return BadRequest(new { message = $"Khung giờ phải nằm trong giờ mở cửa {venue.OpenTime:HH:mm}–{venue.CloseTime:HH:mm}." });

        var utcNow = DateTime.UtcNow;
        var firstStartTime = selectedRanges.Min(item => item.Start);
        var lastEndTime = selectedRanges.Max(item => item.End);
        var staleHoldings = await _bookingRepository.GetStaleHoldingsAsync(selectedCourtIds, utcNow, cancellationToken);
        foreach (var stale in staleHoldings) await ExpireHoldingAsync(stale, "Hết thời gian giữ chỗ", cancellationToken);
        if (staleHoldings.Count > 0) await _bookingRepository.SaveChangesAsync(cancellationToken);

        if (!request.AllowScheduleConflicts)
        {
            var userObj = await _userRepository.GetByIdAsync(player.UserId, cancellationToken);
            var playerName = userObj?.Username ?? "Bạn";
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
        var possiblyOverlappingBookings = await _bookingRepository.GetPotentiallyOverlappingBookingsAsync(
            selectedCourtIds, firstStartTime, lastEndTime, utcNow, cancellationToken);
        var overlaps = possiblyOverlappingBookings.Any(bookingObj => selectedRanges.Any(slot =>
            bookingObj.Slots.Any(existingSlot => existingSlot.CourtId == slot.CourtId && existingSlot.StartTime < slot.End && existingSlot.EndTime > slot.Start)
            || (!bookingObj.Slots.Any() && bookingObj.CourtId == slot.CourtId && bookingObj.StartTime < slot.End && bookingObj.EndTime > slot.Start)));
        if (overlaps) return Conflict(new { message = "Một hoặc nhiều slot vừa được người khác giữ. Hãy tải lại lịch." });

        var holdMinutes = Math.Clamp(_configuration.GetValue("Booking:HoldingMinutes", 5), 1, 60);
        var bankAccount = await _venueRepository.GetOwnerBankAccountAsync(venue.OwnerId, cancellationToken);
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
                    CheckInCode = Services.Bookings.CheckInCode.Next(),
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

        await CheckInCode.EnsureUniqueAsync(
            booking.CheckInGroups.ToList(), _bookingRepository.BookingCheckInGroups, cancellationToken);

        booking.StatusHistories.Add(NewHistory(null, "Holding", "Player tạo giữ chỗ", userId));
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
        payment.StatusHistories.Add(NewPaymentHistory(null, "Pending", "Created", "Tạo yêu cầu chuyển khoản", userId));
        booking.Payments.Add(payment);

        await _bookingRepository.AddAsync(booking, cancellationToken);
        await _bookingRepository.SaveChangesAsync(cancellationToken);
        var response = MapBooking(booking, parentCourt, venue);
        await transaction.CommitAsync(cancellationToken);
        foreach (var slot in booking.Slots)
            _scheduleRealtime.Publish(new ScheduleChangedEvent(venue.VenueId, slot.CourtId, slot.StartTime, slot.EndTime, "Holding", "Created"));

        return Ok(response);
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
        var query = _bookingRepository.GetMyBookingsQueryable(userId.Value);
        var totalCount = await query.CountAsync(cancellationToken);
        var localNow = VietnamTime.Now;
        var utcNow = DateTime.UtcNow;
        var bookings = await query
            .OrderByDescending(booking => booking.CreatedAt)
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

    private const int MaximumScheduleRangeDays = 92;
    private static readonly string[] UnpaidPaymentStatuses = ["Pending", "WaitingForConfirmation"];

    public async Task<ServiceResult<PlayerScheduleResponse>> GetMySchedule(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        if (to < from) return BadRequest(new { message = "Khoảng ngày không hợp lệ." });
        if (to.DayNumber - from.DayNumber >= MaximumScheduleRangeDays)
            return BadRequest(new { message = $"Chỉ xem tối đa {MaximumScheduleRangeDays} ngày mỗi lần." });

        var entries = await _bookingRepository.LoadScheduleEntriesAsync(
            userId.Value,
            from.ToDateTime(TimeOnly.MinValue),
            to.AddDays(1).ToDateTime(TimeOnly.MinValue),
            cancellationToken);

        return Ok(new PlayerScheduleResponse
        {
            FromDate = from,
            ToDate = to,
            Entries = entries.Select(entry => new PlayerScheduleEntryResponse
            {
                EntryType = entry.EntryType,
                ReferenceId = entry.ReferenceId,
                BookingId = entry.BookingId,
                Date = DateOnly.FromDateTime(entry.StartTime),
                StartTime = entry.StartTime,
                EndTime = entry.EndTime,
                VenueId = entry.VenueId,
                VenueName = entry.VenueName,
                Address = entry.Address,
                CourtId = entry.CourtId,
                CourtNumber = entry.CourtNumber,
                Title = entry.Title,
                Status = entry.Status,
                PaymentStatus = entry.PaymentStatus,
                NeedsAction = UnpaidPaymentStatuses.Contains(entry.PaymentStatus),
                Amount = entry.Amount,
                Code = string.IsNullOrWhiteSpace(entry.Code) ? null : entry.Code,
                MatchType = entry.MatchType
            }).ToList()
        });
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

        var bookings = await _bookingRepository.GetHoldingGroupBookingsAsync(paymentGroupId, userId.Value, cancellationToken);
        if (bookings.Count == 0) return NotFound(new { message = "Không tìm thấy nhóm thanh toán." });

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
        await using var transaction = await _bookingRepository.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var booking = await LoadOwnedBookingAsync(bookingId, cancellationToken);
        if (booking is null) return NotFound(new { message = "Không tìm thấy booking." });
        if (booking.Status == "Confirmed") return Ok(MapBooking(booking, booking.Court));
        if (booking.Status != "Holding") return Conflict(new { message = $"Booking đang ở trạng thái {booking.Status}." });
        if (booking.HoldExpiresAt <= DateTime.UtcNow)
        {
            await ExpireHoldingAsync(booking, "Hết thời gian trước khi thanh toán", cancellationToken);
            await _bookingRepository.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            PublishBookingChanged(booking, "Expired", "Deleted");
            return Conflict(new { message = "Thời gian giữ chỗ đã hết. Slot đã được mở lại." });
        }
        if (!await HasPhoneNumberAsync(userId.Value, cancellationToken)) return PhoneNumberRequired();

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
        await _bookingRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        PublishBookingChanged(booking, "Confirmed", "Updated");
        return Ok(MapBooking(booking, booking.Court));
    }

    public async Task<ServiceResult> CancelHolding(int bookingId, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        await using var transaction = await _bookingRepository.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
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
        await _bookingRepository.SaveChangesAsync(cancellationToken);
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
        await using var transaction = await _bookingRepository.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var booking = await LoadOwnedBookingAsync(bookingId, cancellationToken);
        if (booking is null) return NotFound(new { message = "Không tìm thấy booking." });
        if (booking.Status is "Cancelled" or "Expired") return NoContent();
        if (booking.Status is not ("Holding" or "Confirmed"))
            return Conflict(new { message = $"Không thể hủy booking ở trạng thái {booking.Status}." });
        if (booking.Payments.Any(item => item.Status == "Paid"))
            return Conflict(new { message = "Đơn đã thanh toán không thể hủy." });
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
        await _bookingRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        PublishBookingChanged(booking, "Cancelled", "Deleted");
        return NoContent();
    }

    public async Task<ServiceResult<BookingHoldingResponse>> RetryPayment(int bookingId, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        await using var transaction = await _bookingRepository.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var booking = await LoadOwnedBookingAsync(bookingId, cancellationToken);
        if (booking is null) return NotFound(new { message = "Không tìm thấy booking." });
        if (booking.Status != "Holding" || booking.HoldExpiresAt <= DateTime.UtcNow)
            return Conflict(new { message = "Booking không còn trong thời gian giữ chỗ để thanh toán lại." });

        var payment = booking.Payments.OrderByDescending(item => item.PaymentId).FirstOrDefault();
        if (payment is null || payment.Status != "Pending")
            return Conflict(new { message = "Thanh toán chưa ở trạng thái cho phép thử lại." });
        if (!await HasPhoneNumberAsync(userId.Value, cancellationToken)) return PhoneNumberRequired();

        var bankAccount = await _venueRepository.GetOwnerBankAccountAsync(booking.Court.Venue.OwnerId, cancellationToken);
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
        await _bookingRepository.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok(MapBooking(booking, booking.Court));
    }

    private async Task<Booking?> LoadOwnedBookingAsync(int bookingId, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        return userId.HasValue ? await _bookingRepository.GetOwnedBookingAsync(bookingId, userId.Value, cancellationToken) : null;
    }

    private async Task<Booking?> LoadOwnedBookingReadAsync(int bookingId, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        return userId.HasValue ? await _bookingRepository.GetOwnedBookingReadAsync(bookingId, userId.Value, cancellationToken) : null;
    }

    private async Task<bool> HasPhoneNumberAsync(int userId, CancellationToken cancellationToken) =>
        !string.IsNullOrWhiteSpace((await _bookingRepository.GetPlayerByUserIdAsync(userId, cancellationToken))?.PhoneNumber);

    private async Task<Player?> GetOrCreatePlayerAsync(int userId, CancellationToken cancellationToken)
    {
        var player = await _bookingRepository.GetPlayerByUserIdAsync(userId, cancellationToken);
        if (player is not null) return player;
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null || !(user.UserType.Equals("Player", StringComparison.OrdinalIgnoreCase) || user.UserType.Equals("User", StringComparison.OrdinalIgnoreCase))) return null;
        player = new Player { UserId = userId, Prestige = 5, SkillLevel = 0 };
        await _bookingRepository.AddPlayerAsync(player, cancellationToken);
        await _bookingRepository.SaveChangesAsync(cancellationToken);
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

    private static BookingHoldingResponse MapBooking(Booking booking, Court court, Venue? venueOverride = null)
    {
        var venue = venueOverride ?? court.Venue;
        return new()
        {
            BookingId = booking.BookingId,
            BookingCode = booking.BookingCode ?? $"PL-{booking.BookingId}",
            Status = booking.Status,
            CreatedAt = AsUtc(booking.CreatedAt),
            HoldExpiresAt = AsUtc(booking.HoldExpiresAt),
            VenueId = venue.VenueId,
            VenueName = venue.VenueName,
            Address = venue.Address,
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
    }

    public void SetCurrentUserId(int? userId) => _currentUserId = userId;

    private int? _currentUserId;

    private int? CurrentUserId() => _currentUserId;

    private static List<string> SplitReviewTags(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

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
        FromStatus = from,
        ToStatus = to,
        Action = action,
        Reason = reason,
        ActorUserId = actorUserId,
        CreatedAt = DateTime.UtcNow
    };

    private static string BuildVietQrUrl(OwnerBankAccount account, decimal amount, string content)
    {
        var query = $"amount={Math.Round(amount):0}&addInfo={Uri.EscapeDataString(content)}&accountName={Uri.EscapeDataString(account.AccountHolderName)}";
        return $"https://img.vietqr.io/image/{Uri.EscapeDataString(account.BankCode)}-{Uri.EscapeDataString(account.AccountNumber)}-compact2.png?{query}";
    }

    private static decimal GetBasePrice(Venue venue) => decimal.TryParse(venue.BookingRules.FirstOrDefault(rule => rule.RuleType == "BasePrice")?.RuleContent, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : 0;
    private static decimal RoundMoney(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero);
}
