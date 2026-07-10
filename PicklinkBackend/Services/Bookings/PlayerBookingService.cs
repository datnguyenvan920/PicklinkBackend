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
        double? minPrice,
        double? maxPrice,
        bool favoritesOnly = false,
        int page = 1,
        int pageSize = Pagination.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        if (minPrice is < 0 || maxPrice is < 0 || (minPrice.HasValue && maxPrice.HasValue && minPrice > maxPrice))
            return BadRequest(new { message = "KhoÃƒÂ¡Ã‚ÂºÃ‚Â£ng giÃƒÆ’Ã‚Â¡ khÃƒÆ’Ã‚Â´ng hÃƒÂ¡Ã‚Â»Ã‚Â£p lÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¡." });

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
            return NotFound(new { message = "KhÃƒÆ’Ã‚Â´ng tÃƒÆ’Ã‚Â¬m thÃƒÂ¡Ã‚ÂºÃ‚Â¥y cÃƒÂ¡Ã‚Â»Ã‚Â¥m sÃƒÆ’Ã‚Â¢n." });
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
            .Include(item => item.Courts)
            .Include(item => item.BookingRules)
            .SingleOrDefaultAsync(
                venue => venue.VenueId == venueId
                    && venue.ApprovalStatus == "Approved",
                cancellationToken);
        if (venue is null) return NotFound(new { message = "KhÃƒÆ’Ã‚Â´ng tÃƒÆ’Ã‚Â¬m thÃƒÂ¡Ã‚ÂºÃ‚Â¥y cÃƒÂ¡Ã‚Â»Ã‚Â¥m sÃƒÆ’Ã‚Â¢n." });

        var dayStart = date.ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);
        var currentUserId = CurrentUserId();
        var now = DateTime.UtcNow;
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
                CourtType = court.CourtType ?? "TiÃƒÆ’Ã‚Âªu chuÃƒÂ¡Ã‚ÂºÃ‚Â©n",
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
    public async Task<ServiceResult<BookingHoldingResponse>> CreateHolding(
        CreateBookingHoldRequest request,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var player = await GetOrCreatePlayerAsync(userId.Value, cancellationToken);
        if (player is null) return BadRequest(new { message = "TÃƒÆ’Ã‚Â i khoÃƒÂ¡Ã‚ÂºÃ‚Â£n chÃƒâ€ Ã‚Â°a cÃƒÆ’Ã‚Â³ hÃƒÂ¡Ã‚Â»Ã¢â‚¬Å“ sÃƒâ€ Ã‚Â¡ ngÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Âi chÃƒâ€ Ã‚Â¡i." });

        var selectedSlots = request.SlotStarts.Distinct().OrderBy(item => item).ToList();
        if (selectedSlots.Count != request.SlotStarts.Count)
            return BadRequest(new { message = "Danh sÃƒÆ’Ã‚Â¡ch slot bÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¹ trÃƒÆ’Ã‚Â¹ng." });
        for (var index = 1; index < selectedSlots.Count; index++)
            if (selectedSlots[index].ToTimeSpan() - selectedSlots[index - 1].ToTimeSpan() != TimeSpan.FromMinutes(30))
                return BadRequest(new { message = "ChÃƒÂ¡Ã‚Â»Ã¢â‚¬Â° Ãƒâ€žÃ¢â‚¬ËœÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Â£c chÃƒÂ¡Ã‚Â»Ã‚Ân cÃƒÆ’Ã‚Â¡c slot 30 phÃƒÆ’Ã‚Âºt liÃƒÆ’Ã‚Âªn tiÃƒÂ¡Ã‚ÂºÃ‚Â¿p." });
        if (selectedSlots.Any(slot => slot.Minute % 30 != 0 || slot.Second != 0))
            return BadRequest(new { message = "Slot phÃƒÂ¡Ã‚ÂºÃ‚Â£i bÃƒÂ¡Ã‚ÂºÃ‚Â¯t Ãƒâ€žÃ¢â‚¬ËœÃƒÂ¡Ã‚ÂºÃ‚Â§u tÃƒÂ¡Ã‚ÂºÃ‚Â¡i phÃƒÆ’Ã‚Âºt 00 hoÃƒÂ¡Ã‚ÂºÃ‚Â·c 30." });

        var startTime = request.Date.ToDateTime(selectedSlots[0]);
        var endTime = request.Date.ToDateTime(selectedSlots[^1]).AddMinutes(30);
        if (startTime <= DateTime.Now)
            return BadRequest(new { message = "KhÃƒÆ’Ã‚Â´ng thÃƒÂ¡Ã‚Â»Ã†â€™ giÃƒÂ¡Ã‚Â»Ã‚Â¯ chÃƒÂ¡Ã‚Â»Ã¢â‚¬â€ cho khung giÃƒÂ¡Ã‚Â»Ã‚Â Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ qua." });

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(_dbContext, transaction, $"court-booking:{request.CourtId}", cancellationToken))
            return Conflict(new { message = "SÃƒÆ’Ã‚Â¢n Ãƒâ€žÃ¢â‚¬Ëœang Ãƒâ€žÃ¢â‚¬ËœÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Â£c ngÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Âi khÃƒÆ’Ã‚Â¡c thao tÃƒÆ’Ã‚Â¡c. Vui lÃƒÆ’Ã‚Â²ng thÃƒÂ¡Ã‚Â»Ã‚Â­ lÃƒÂ¡Ã‚ÂºÃ‚Â¡i." });
        if (!await SqlServerBookingLock.AcquireAsync(_dbContext, transaction, $"player-schedule:{player.PlayerId}", cancellationToken))
            return Conflict(new { message = "LÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¹ch cÃƒÂ¡Ã‚Â»Ã‚Â§a bÃƒÂ¡Ã‚ÂºÃ‚Â¡n Ãƒâ€žÃ¢â‚¬Ëœang Ãƒâ€žÃ¢â‚¬ËœÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Â£c cÃƒÂ¡Ã‚ÂºÃ‚Â­p nhÃƒÂ¡Ã‚ÂºÃ‚Â­t. Vui lÃƒÆ’Ã‚Â²ng thÃƒÂ¡Ã‚Â»Ã‚Â­ lÃƒÂ¡Ã‚ÂºÃ‚Â¡i." });

        var court = await _dbContext.Courts
            .Include(item => item.Venue).ThenInclude(venue => venue.BookingRules)
            .SingleOrDefaultAsync(item => item.CourtId == request.CourtId, cancellationToken);
        if (court is null) return NotFound(new { message = "KhÃƒÆ’Ã‚Â´ng tÃƒÆ’Ã‚Â¬m thÃƒÂ¡Ã‚ÂºÃ‚Â¥y sÃƒÆ’Ã‚Â¢n con." });
        if (court.Venue.ApprovalStatus != "Approved"
            || !court.Venue.IsOpen
            || court.AvailabilityStatus != "Available")
            return Conflict(new { message = "SÃƒÆ’Ã‚Â¢n hiÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¡n khÃƒÆ’Ã‚Â´ng nhÃƒÂ¡Ã‚ÂºÃ‚Â­n Ãƒâ€žÃ¢â‚¬ËœÃƒÂ¡Ã‚ÂºÃ‚Â·t chÃƒÂ¡Ã‚Â»Ã¢â‚¬â€." });

        var opening = request.Date.ToDateTime(court.Venue.OpenTime);
        var closing = request.Date.ToDateTime(court.Venue.CloseTime);
        if (startTime < opening || endTime > closing)
            return BadRequest(new { message = $"Khung giÃƒÂ¡Ã‚Â»Ã‚Â phÃƒÂ¡Ã‚ÂºÃ‚Â£i nÃƒÂ¡Ã‚ÂºÃ‚Â±m trong giÃƒÂ¡Ã‚Â»Ã‚Â mÃƒÂ¡Ã‚Â»Ã…Â¸ cÃƒÂ¡Ã‚Â»Ã‚Â­a {court.Venue.OpenTime:HH:mm}ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Å“{court.Venue.CloseTime:HH:mm}." });

        var utcNow = DateTime.UtcNow;
        var staleHoldings = await _dbContext.Bookings
            .Include(booking => booking.Payments).ThenInclude(payment => payment.StatusHistories)
            .Where(booking => booking.CourtId == request.CourtId && booking.Status == "Holding" && booking.HoldExpiresAt <= utcNow)
            .ToListAsync(cancellationToken);
        foreach (var stale in staleHoldings) await ExpireHoldingAsync(stale, "HÃƒÂ¡Ã‚ÂºÃ‚Â¿t thÃƒÂ¡Ã‚Â»Ã‚Âi gian giÃƒÂ¡Ã‚Â»Ã‚Â¯ chÃƒÂ¡Ã‚Â»Ã¢â‚¬â€", cancellationToken);
        if (staleHoldings.Count > 0) await _dbContext.SaveChangesAsync(cancellationToken);

        if (await _playerScheduleConflict.HasConflictAsync(
                player.PlayerId,
                startTime,
                endTime,
                cancellationToken: cancellationToken))
            return Conflict(new { message = "BÃƒÂ¡Ã‚ÂºÃ‚Â¡n Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ cÃƒÆ’Ã‚Â³ lÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¹ch Ãƒâ€žÃ¢â‚¬ËœÃƒÂ¡Ã‚ÂºÃ‚Â·t sÃƒÆ’Ã‚Â¢n hoÃƒÂ¡Ã‚ÂºÃ‚Â·c ghÃƒÆ’Ã‚Â©p trÃƒÂ¡Ã‚ÂºÃ‚Â­n trÃƒÆ’Ã‚Â¹ng vÃƒÂ¡Ã‚Â»Ã¢â‚¬Âºi khung giÃƒÂ¡Ã‚Â»Ã‚Â nÃƒÆ’Ã‚Â y." });

        var overlaps = await _dbContext.Bookings.AnyAsync(booking =>
            booking.CourtId == request.CourtId && !InactiveStatuses.Contains(booking.Status) &&
            (booking.Status != "Holding" || booking.HoldExpiresAt > utcNow) &&
            booking.StartTime < endTime && booking.EndTime > startTime,
            cancellationToken);
        if (overlaps) return Conflict(new { message = "MÃƒÂ¡Ã‚Â»Ã¢â€žÂ¢t hoÃƒÂ¡Ã‚ÂºÃ‚Â·c nhiÃƒÂ¡Ã‚Â»Ã‚Âu slot vÃƒÂ¡Ã‚Â»Ã‚Â«a Ãƒâ€žÃ¢â‚¬ËœÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Â£c ngÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Âi khÃƒÆ’Ã‚Â¡c giÃƒÂ¡Ã‚Â»Ã‚Â¯. HÃƒÆ’Ã‚Â£y tÃƒÂ¡Ã‚ÂºÃ‚Â£i lÃƒÂ¡Ã‚ÂºÃ‚Â¡i lÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¹ch." });

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
        booking.StatusHistories.Add(NewHistory(null, "Holding", "Player tÃƒÂ¡Ã‚ÂºÃ‚Â¡o giÃƒÂ¡Ã‚Â»Ã‚Â¯ chÃƒÂ¡Ã‚Â»Ã¢â‚¬â€", userId));
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
        payment.StatusHistories.Add(NewPaymentHistory(null, "Pending", "Created", "TÃƒÂ¡Ã‚ÂºÃ‚Â¡o yÃƒÆ’Ã‚Âªu cÃƒÂ¡Ã‚ÂºÃ‚Â§u chuyÃƒÂ¡Ã‚Â»Ã†â€™n khoÃƒÂ¡Ã‚ÂºÃ‚Â£n", userId));
        booking.Payments.Add(payment);
        _dbContext.Bookings.Add(booking);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _scheduleRealtime.Publish(new ScheduleChangedEvent(court.VenueId, court.CourtId, booking.StartTime, booking.EndTime, "Holding", "Created"));

        return Ok(MapBooking(booking, court));
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
    public async Task<ServiceResult<BookingHoldingResponse>> GetBooking(int bookingId, CancellationToken cancellationToken)
    {
        var booking = await LoadOwnedBookingReadAsync(bookingId, cancellationToken);
        return booking is null ? NotFound(new { message = "KhÃƒÆ’Ã‚Â´ng tÃƒÆ’Ã‚Â¬m thÃƒÂ¡Ã‚ÂºÃ‚Â¥y booking." }) : Ok(MapBooking(booking, booking.Court));
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
            return Conflict(new { message = "Booking Ãƒâ€žÃ¢â‚¬Ëœang Ãƒâ€žÃ¢â‚¬ËœÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Â£c xÃƒÂ¡Ã‚Â»Ã‚Â­ lÃƒÆ’Ã‚Â½. Vui lÃƒÆ’Ã‚Â²ng thÃƒÂ¡Ã‚Â»Ã‚Â­ lÃƒÂ¡Ã‚ÂºÃ‚Â¡i." });

        var booking = await LoadOwnedBookingAsync(bookingId, cancellationToken);
        if (booking is null) return NotFound(new { message = "KhÃƒÆ’Ã‚Â´ng tÃƒÆ’Ã‚Â¬m thÃƒÂ¡Ã‚ÂºÃ‚Â¥y booking." });
        if (booking.Status == "Confirmed") return Ok(MapBooking(booking, booking.Court));
        if (booking.Status != "Holding") return Conflict(new { message = $"Booking Ãƒâ€žÃ¢â‚¬Ëœang ÃƒÂ¡Ã‚Â»Ã…Â¸ trÃƒÂ¡Ã‚ÂºÃ‚Â¡ng thÃƒÆ’Ã‚Â¡i {booking.Status}." });
        if (booking.HoldExpiresAt <= DateTime.UtcNow)
        {
            await ExpireHoldingAsync(booking, "HÃƒÂ¡Ã‚ÂºÃ‚Â¿t thÃƒÂ¡Ã‚Â»Ã‚Âi gian trÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã¢â‚¬Âºc khi thanh toÃƒÆ’Ã‚Â¡n", cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            PublishBookingChanged(booking, "Expired", "Deleted");
            return Conflict(new { message = "ThÃƒÂ¡Ã‚Â»Ã‚Âi gian giÃƒÂ¡Ã‚Â»Ã‚Â¯ chÃƒÂ¡Ã‚Â»Ã¢â‚¬â€ Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ hÃƒÂ¡Ã‚ÂºÃ‚Â¿t. Slot Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ Ãƒâ€žÃ¢â‚¬ËœÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Â£c mÃƒÂ¡Ã‚Â»Ã…Â¸ lÃƒÂ¡Ã‚ÂºÃ‚Â¡i." });
        }

        if (request.PaymentMethod == "BankTransfer")
            return Conflict(new { message = "ChuyÃƒÂ¡Ã‚Â»Ã†â€™n khoÃƒÂ¡Ã‚ÂºÃ‚Â£n ngÃƒÆ’Ã‚Â¢n hÃƒÆ’Ã‚Â ng cÃƒÂ¡Ã‚ÂºÃ‚Â§n gÃƒÂ¡Ã‚Â»Ã‚Â­i biÃƒÆ’Ã‚Âªn lai vÃƒÆ’Ã‚Â  chÃƒÂ¡Ã‚Â»Ã‚Â chÃƒÂ¡Ã‚Â»Ã‚Â§ sÃƒÆ’Ã‚Â¢n xÃƒÆ’Ã‚Â¡c nhÃƒÂ¡Ã‚ÂºÃ‚Â­n." });

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
            payment.StatusHistories.Add(NewPaymentHistory(previousPaymentStatus, "Pending", "AtCourtSelected", "KhÃƒÆ’Ã‚Â¡ch chÃƒÂ¡Ã‚Â»Ã‚Ân thanh toÃƒÆ’Ã‚Â¡n tÃƒÂ¡Ã‚ÂºÃ‚Â¡i sÃƒÆ’Ã‚Â¢n", userId));
            booking.StatusHistories.Add(NewHistory(previous, "Confirmed", "GiÃƒÂ¡Ã‚Â»Ã‚Â¯ sÃƒÆ’Ã‚Â¢n - chÃƒÂ¡Ã‚Â»Ã‚Â thanh toÃƒÆ’Ã‚Â¡n tÃƒÂ¡Ã‚ÂºÃ‚Â¡i quÃƒÂ¡Ã‚ÂºÃ‚Â§y", userId));
        }
        else
        {
            payment.Status = "Paid";
            payment.PaidAt = DateTime.UtcNow;
            payment.StatusHistories.Add(NewPaymentHistory(previousPaymentStatus, "Paid", "LegacyPaymentCompleted", $"Thanh toÃƒÆ’Ã‚Â¡n {request.PaymentMethod}", userId));
            booking.StatusHistories.Add(NewHistory(previous, "Confirmed", $"Thanh toÃƒÆ’Ã‚Â¡n {request.PaymentMethod} thÃƒÆ’Ã‚Â nh cÃƒÆ’Ã‚Â´ng", userId));
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
            return Conflict(new { message = "Booking Ãƒâ€žÃ¢â‚¬Ëœang Ãƒâ€žÃ¢â‚¬ËœÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Â£c xÃƒÂ¡Ã‚Â»Ã‚Â­ lÃƒÆ’Ã‚Â½." });
        var booking = await LoadOwnedBookingAsync(bookingId, cancellationToken);
        if (booking is null) return NotFound(new { message = "KhÃƒÆ’Ã‚Â´ng tÃƒÆ’Ã‚Â¬m thÃƒÂ¡Ã‚ÂºÃ‚Â¥y booking." });
        if (booking.Status != "Holding") return Conflict(new { message = "ChÃƒÂ¡Ã‚Â»Ã¢â‚¬Â° cÃƒÆ’Ã‚Â³ thÃƒÂ¡Ã‚Â»Ã†â€™ hÃƒÂ¡Ã‚Â»Ã‚Â§y booking Ãƒâ€žÃ¢â‚¬Ëœang giÃƒÂ¡Ã‚Â»Ã‚Â¯ chÃƒÂ¡Ã‚Â»Ã¢â‚¬â€." });
        booking.Status = "Cancelled";
        booking.HoldExpiresAt = null;
        foreach (var payment in booking.Payments.Where(item => item.Status is "Pending" or "WaitingForConfirmation"))
        {
            var fromPaymentStatus = payment.Status;
            payment.Status = "Cancelled";
            payment.StatusHistories.Add(NewPaymentHistory(fromPaymentStatus, "Cancelled", "BookingCancelled", "Player hÃƒÂ¡Ã‚Â»Ã‚Â§y giÃƒÂ¡Ã‚Â»Ã‚Â¯ chÃƒÂ¡Ã‚Â»Ã¢â‚¬â€", userId));
        }
        booking.StatusHistories.Add(NewHistory("Holding", "Cancelled", "Player hÃƒÂ¡Ã‚Â»Ã‚Â§y giÃƒÂ¡Ã‚Â»Ã‚Â¯ chÃƒÂ¡Ã‚Â»Ã¢â‚¬â€", userId));
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
            return Conflict(new { message = "Booking Ãƒâ€žÃ¢â‚¬Ëœang Ãƒâ€žÃ¢â‚¬ËœÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Â£c xÃƒÂ¡Ã‚Â»Ã‚Â­ lÃƒÆ’Ã‚Â½." });

        var booking = await LoadOwnedBookingAsync(bookingId, cancellationToken);
        if (booking is null) return NotFound(new { message = "KhÃƒÆ’Ã‚Â´ng tÃƒÆ’Ã‚Â¬m thÃƒÂ¡Ã‚ÂºÃ‚Â¥y booking." });
        if (booking.Status is "Cancelled" or "Expired") return NoContent();
        if (booking.Status is not ("Holding" or "Confirmed"))
            return Conflict(new { message = $"KhÃƒÆ’Ã‚Â´ng thÃƒÂ¡Ã‚Â»Ã†â€™ hÃƒÂ¡Ã‚Â»Ã‚Â§y booking ÃƒÂ¡Ã‚Â»Ã…Â¸ trÃƒÂ¡Ã‚ÂºÃ‚Â¡ng thÃƒÆ’Ã‚Â¡i {booking.Status}." });
        if (DateTime.Now >= booking.StartTime)
            return Conflict(new { message = "KhÃƒÆ’Ã‚Â´ng thÃƒÂ¡Ã‚Â»Ã†â€™ hÃƒÂ¡Ã‚Â»Ã‚Â§y booking Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ Ãƒâ€žÃ¢â‚¬ËœÃƒÂ¡Ã‚ÂºÃ‚Â¿n giÃƒÂ¡Ã‚Â»Ã‚Â chÃƒâ€ Ã‚Â¡i." });
        if (booking.Operation?.CheckInStatus == "CheckedIn")
            return Conflict(new { message = "Booking Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ check-in nÃƒÆ’Ã‚Âªn khÃƒÆ’Ã‚Â´ng thÃƒÂ¡Ã‚Â»Ã†â€™ hÃƒÂ¡Ã‚Â»Ã‚Â§y." });

        var cancellationReason = request.Reason.Trim();
        var previous = booking.Status;
        booking.Status = "Cancelled";
        booking.HoldExpiresAt = null;
        foreach (var payment in booking.Payments.Where(item => item.Status is "Pending" or "WaitingForConfirmation"))
        {
            var fromPaymentStatus = payment.Status;
            payment.Status = "Cancelled";
            payment.StatusHistories.Add(NewPaymentHistory(fromPaymentStatus, "Cancelled", "BookingCancelled", $"Player hÃƒÂ¡Ã‚Â»Ã‚Â§y booking: {cancellationReason}", userId));
        }
        booking.StatusHistories.Add(NewHistory(previous, "Cancelled", $"Player hÃƒÂ¡Ã‚Â»Ã‚Â§y booking: {cancellationReason}", userId));
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
            return Conflict(new { message = "Booking Ãƒâ€žÃ¢â‚¬Ëœang Ãƒâ€žÃ¢â‚¬ËœÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Â£c xÃƒÂ¡Ã‚Â»Ã‚Â­ lÃƒÆ’Ã‚Â½." });

        var booking = await LoadOwnedBookingAsync(bookingId, cancellationToken);
        if (booking is null) return NotFound(new { message = "KhÃƒÆ’Ã‚Â´ng tÃƒÆ’Ã‚Â¬m thÃƒÂ¡Ã‚ÂºÃ‚Â¥y booking." });
        if (booking.Status != "Holding" || booking.HoldExpiresAt <= DateTime.UtcNow)
            return Conflict(new { message = "Booking khÃƒÆ’Ã‚Â´ng cÃƒÆ’Ã‚Â²n trong thÃƒÂ¡Ã‚Â»Ã‚Âi gian giÃƒÂ¡Ã‚Â»Ã‚Â¯ chÃƒÂ¡Ã‚Â»Ã¢â‚¬â€ Ãƒâ€žÃ¢â‚¬ËœÃƒÂ¡Ã‚Â»Ã†â€™ thanh toÃƒÆ’Ã‚Â¡n lÃƒÂ¡Ã‚ÂºÃ‚Â¡i." });

        var payment = booking.Payments.OrderByDescending(item => item.PaymentId).FirstOrDefault();
        if (payment is null || payment.Status != "Pending")
            return Conflict(new { message = "Thanh toÃƒÆ’Ã‚Â¡n chÃƒâ€ Ã‚Â°a ÃƒÂ¡Ã‚Â»Ã…Â¸ trÃƒÂ¡Ã‚ÂºÃ‚Â¡ng thÃƒÆ’Ã‚Â¡i cho phÃƒÆ’Ã‚Â©p thÃƒÂ¡Ã‚Â»Ã‚Â­ lÃƒÂ¡Ã‚ÂºÃ‚Â¡i." });

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
        payment.StatusHistories.Add(NewPaymentHistory("Pending", "Pending", "RetryRequested", "Player yÃƒÆ’Ã‚Âªu cÃƒÂ¡Ã‚ÂºÃ‚Â§u thanh toÃƒÆ’Ã‚Â¡n lÃƒÂ¡Ã‚ÂºÃ‚Â¡i", userId));
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
