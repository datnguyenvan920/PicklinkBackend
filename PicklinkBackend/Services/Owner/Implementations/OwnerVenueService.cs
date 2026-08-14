using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Repositories;
using PicklinkBackend.Services.Bookings;
using PicklinkBackend.Services.Owner;
using PicklinkBackend.Services.Schedules;
using PicklinkBackend.Services.Shared;
using PicklinkBackend.Services.Venues;

namespace PicklinkBackend.Services.Owner.Implementations;

public sealed record OwnerVenueServiceDependencies(
    IVenueRepository VenueRepository,
    IUserRepository UserRepository,
    IPaymentRepository PaymentRepository,
    IWebHostEnvironment Environment,
    IConfiguration Configuration,
    ScheduleRealtimeNotifier ScheduleRealtime,
    VenueRealtimeNotifier VenueRealtime,
    CloudinaryUploadService CloudinaryUpload);

public class OwnerVenueService : IOwnerVenueService
{
    private const long MaxVenueImageBytes = 5 * 1024 * 1024;
    private sealed record SchedulePayment(int BookingId, string Status);

    private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };
    private static readonly HashSet<string> AllowedImageContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp"
    };

    private readonly IVenueRepository _venueRepository;
    private readonly IUserRepository _userRepository;
    private readonly IPaymentRepository _paymentRepository;
    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly ScheduleRealtimeNotifier _scheduleRealtime;
    private readonly VenueRealtimeNotifier _venueRealtime;
    private readonly CloudinaryUploadService _cloudinaryUpload;
    private int? _currentUserId;

    private OwnerVenueService(
        IVenueRepository venueRepository,
        IUserRepository userRepository,
        IPaymentRepository paymentRepository,
        IWebHostEnvironment environment,
        IConfiguration configuration,
        ScheduleRealtimeNotifier scheduleRealtime,
        VenueRealtimeNotifier venueRealtime,
        CloudinaryUploadService cloudinaryUpload)
    {
        _venueRepository = venueRepository;
        _userRepository = userRepository;
        _paymentRepository = paymentRepository;
        _environment = environment;
        _configuration = configuration;
        _scheduleRealtime = scheduleRealtime;
        _venueRealtime = venueRealtime;
        _cloudinaryUpload = cloudinaryUpload;
    }

    public OwnerVenueService(OwnerVenueServiceDependencies dependencies)
        : this(
            dependencies.VenueRepository,
            dependencies.UserRepository,
            dependencies.PaymentRepository,
            dependencies.Environment,
            dependencies.Configuration,
            dependencies.ScheduleRealtime,
            dependencies.VenueRealtime,
            dependencies.CloudinaryUpload)
    {
    }

    public void SetCurrentUserId(int? userId)
    {
        _currentUserId = userId;
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

    private static ServiceResult<T> CreatedAtAction<T>(string actionName, object routeValues, T value) =>
        new(ServiceResultStatus.Created, value, CreatedActionName: actionName, CreatedRouteValues: routeValues);

    public async Task<ServiceResult<List<OwnerVenueResponse>>> GetVenues(CancellationToken cancellationToken)
    {
        var owner = await GetOwnerAsync(false, cancellationToken);
        if (owner is null) return Ok(new List<OwnerVenueResponse>());

        var venues = await LoadOwnerVenues(owner.OwnerId, cancellationToken);
        return Ok(venues.Select(MapVenue).ToList());
    }

    public async Task<ServiceResult<OwnerVenueResponse>> GetVenue(int venueId, CancellationToken cancellationToken)
    {
        var venue = await GetOwnedVenue(venueId, cancellationToken);
        return venue is null ? NotFound(new { message = "Không tìm thấy cụm sân." }) : Ok(MapVenue(venue));
    }

    public async Task<ServiceResult<List<OwnerVenueReviewResponse>>> GetVenueReviews(
        int venueId,
        CancellationToken cancellationToken)
    {
        var venue = await GetOwnedVenue(venueId, cancellationToken);
        if (venue is null) return NotFound(new { message = "Không tìm thấy cụm sân." });

        var reviews = await _venueRepository.RatingHistories
            .AsNoTracking()
            .Include(review => review.User)
            .Include(review => review.Booking)
                .ThenInclude(booking => booking!.Court)
            .Where(review => review.TargetType == "Venue"
                && review.TargetId == venueId
                && !review.IsHidden
                && review.ModerationStatus == "Visible")
            .OrderByDescending(review => review.CreatedAt)
            .ToListAsync(cancellationToken);

        return Ok(reviews.Select(MapOwnerReview).ToList());
    }

    public async Task<ServiceResult<OwnerVenueResponse>> CreateVenue(
        OwnerVenueUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (request.CloseTime <= request.OpenTime)
            return BadRequest(new { message = "Giờ đóng cửa phải sau giờ mở cửa." });

        var owner = await GetOwnerAsync(true, cancellationToken);
        if (owner is null) return Unauthorized();

        var venue = new Venue
        {
            OwnerId = owner.OwnerId,
            VenueName = request.VenueName.Trim(),
            Address = request.Address.Trim(),
            OpenTime = request.OpenTime,
            CloseTime = request.CloseTime,
            PhoneNumber = Normalize(request.PhoneNumber),
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            OverallRating = 0,
            IsOpen = true,
            ApprovalStatus = "Draft"
        };

        await _venueRepository.AddVenueAsync(venue, cancellationToken);
        await _venueRepository.SaveChangesAsync(cancellationToken);
        ApplyVenueDetails(venue, request);

        for (var number = 1; number <= request.InitialCourtCount; number++)
        {
            await _venueRepository.AddCourtAsync(new Court
            {
                VenueId = venue.VenueId,
                CourtNumber = number,
                CourtType = "Standard",
                SurfaceType = "Hard court",
                HourlyPrice = request.BasePrice,
                AvailabilityStatus = "Available"
            }, cancellationToken);
        }

        await _venueRepository.SaveChangesAsync(cancellationToken);
        _venueRealtime.Publish(venue.VenueId, "Created");
        return CreatedAtAction(nameof(GetVenue), new { venueId = venue.VenueId }, MapVenue(venue));
    }

    public async Task<ServiceResult<OwnerVenueResponse>> UpdateVenue(
        int venueId,
        OwnerVenueUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (request.CloseTime <= request.OpenTime)
            return BadRequest(new { message = "Giờ đóng cửa phải sau giờ mở cửa." });

        var venue = await GetOwnedVenue(venueId, cancellationToken);
        if (venue is null) return NotFound(new { message = "Không tìm thấy cụm sân." });

        venue.VenueName = request.VenueName.Trim();
        venue.Address = request.Address.Trim();
        venue.OpenTime = request.OpenTime;
        venue.CloseTime = request.CloseTime;
        venue.PhoneNumber = Normalize(request.PhoneNumber);
        venue.Latitude = request.Latitude;
        venue.Longitude = request.Longitude;
        VenueApprovalWorkflow.MarkChangedByOwner(venue);
        ApplyVenueDetails(venue, request);

        await _venueRepository.SaveChangesAsync(cancellationToken);
        _venueRealtime.Publish(venueId, "Updated");
        return Ok(MapVenue(venue));
    }

    public async Task<ServiceResult<OwnerVenueResponse>> SetVenueOpenStatus(
        int venueId,
        OwnerVenueOpenStatusRequest request,
        CancellationToken cancellationToken)
    {
        var venue = await GetOwnedVenue(venueId, cancellationToken);
        if (venue is null) return NotFound(new { message = "Không tìm thấy cụm sân." });

        venue.IsOpen = request.IsOpen;
        AddAuditLog(venue, request.IsOpen ? "OwnerOpenedVenue" : "OwnerClosedVenue");
        await _venueRepository.SaveChangesAsync(cancellationToken);
        _venueRealtime.Publish(venueId, request.IsOpen ? "Opened" : "Closed");
        return Ok(MapVenue(venue));
    }

    public async Task<ServiceResult<OwnerVenueResponse>> SubmitVenueForApproval(
        int venueId,
        CancellationToken cancellationToken)
    {
        var venue = await GetOwnedVenue(venueId, cancellationToken);
        if (venue is null) return NotFound(new { message = "Không tìm thấy cụm sân." });
        if (venue.ApprovalStatus == "Approved")
            return Conflict(new { message = "Cụm sân đã được Admin duyệt." });
        if (venue.ApprovalStatus == "Pending")
            return Conflict(new { message = "Cụm sân đang chờ Admin duyệt." });
        if (ActiveCourtCount(venue) == 0)
            return BadRequest(new { message = "Hãy thêm ít nhất một sân con trước khi gửi duyệt." });
        if (venue.Latitude is null || venue.Longitude is null)
            return BadRequest(new { message = "Hãy định vị cụm sân trên bản đồ trước khi gửi duyệt." });

        venue.ApprovalStatus = "Pending";
        venue.RejectionReason = null;
        AddAuditLog(venue, "OwnerSubmittedForApproval");
        await _venueRepository.SaveChangesAsync(cancellationToken);
        _venueRealtime.Publish(venueId, "Submitted");
        return Ok(MapVenue(venue));
    }

    public async Task<ServiceResult<OwnerListingFeePreviewResponse>> PreviewListingFee(
        int venueId,
        int months = 1,
        CancellationToken cancellationToken = default)
    {
        if (months is < 1 or > 24)
            return BadRequest(new { message = "Số tháng phải từ 1 đến 24." });

        var venue = await GetOwnedVenue(venueId, cancellationToken);
        if (venue is null) return NotFound(new { message = "Không tìm thấy cụm sân." });

        var activeCourts = ActiveCourtCount(venue);
        if (activeCourts == 0)
            return BadRequest(new { message = "Cụm sân chưa có sân con đang hoạt động để tính phí hiển thị." });

        var pricePerCourt = await GetCurrentListingPriceAsync(cancellationToken);
        var totalAmount = activeCourts * pricePerCourt * months;
        return Ok(new OwnerListingFeePreviewResponse
        {
            VenueId = venueId,
            VenueName = venue.VenueName,
            Months = months,
            ActiveCourtCount = activeCourts,
            PricePerCourtPerMonth = pricePerCourt,
            TotalAmount = totalAmount
        });
    }

    public async Task<ServiceResult<OwnerListingFeePaymentResponse>> SubmitListingFeePayment(
        int venueId,
        OwnerListingFeePaymentRequest request,
        CancellationToken cancellationToken)
    {
        return await SubmitListingFeePayment(venueId, request.Months, request.Receipt, cancellationToken);
    }

    public async Task<ServiceResult<OwnerListingFeePaymentResponse>> SubmitListingFeePayment(
        int venueId,
        int months,
        IFormFile receipt,
        CancellationToken cancellationToken = default)
    {
        if (months is < 1 or > 24)
            return BadRequest(new { message = "Số tháng phải từ 1 đến 24." });
        if (receipt is null || receipt.Length == 0)
            return BadRequest(new { message = "Hãy tải lên ảnh biên lai thanh toán." });
        if (receipt.Length > MaxVenueImageBytes)
            return BadRequest(new { message = "Dung lượng ảnh biên lai tối đa 5MB." });

        var venue = await GetOwnedVenue(venueId, cancellationToken);
        if (venue is null) return NotFound(new { message = "Không tìm thấy cụm sân." });

        var activeCourts = ActiveCourtCount(venue);
        if (activeCourts == 0)
            return BadRequest(new { message = "Cụm sân chưa có sân con đang hoạt động để tính phí hiển thị." });

        var pricePerCourt = await GetCurrentListingPriceAsync(cancellationToken);
        var totalAmount = activeCourts * pricePerCourt * months;

        var payment = new VenueListingPayment
        {
            VenueId = venueId,
            Months = months,
            ActiveCourtCount = activeCourts,
            PricePerCourtPerMonth = pricePerCourt,
            Amount = totalAmount,
            Status = "PendingReview",
            SubmittedAt = DateTime.UtcNow
        };

        venue.VenueListingPayments.Add(payment);
        await _venueRepository.SaveChangesAsync(cancellationToken);

        string receiptUrl;
        try
        {
            receiptUrl = await SaveListingFeeReceiptAsync(payment.VenueListingPaymentId, receipt, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        payment.ReceiptImageUrl = receiptUrl;
        AddAuditLog(venue, $"SubmittedListingFeePayment:{payment.VenueListingPaymentId}");
        await _venueRepository.SaveChangesAsync(cancellationToken);
        _venueRealtime.Publish(venueId, "ListingFeeSubmitted");

        return Ok(MapListingPayment(payment));
    }

    public async Task<ServiceResult<OwnerVenueImageResponse>> UploadVenueImage(
        int venueId, OwnerVenueImageUploadRequest request, CancellationToken cancellationToken)
    {
        var result = await UploadVenueImages(venueId, new List<IFormFile> { request.File }, cancellationToken);
        if (result.Status != ServiceResultStatus.Success) return new ServiceResult<OwnerVenueImageResponse>(result.Status, Error: result.Error);
        var list = (List<OwnerVenueImageResponse>)result.Value!;
        return Ok(list[0]);
    }

    public async Task<ServiceResult<OwnerVenueResponse>> SetPrimaryVenueImage(
        int venueId, int imageId, CancellationToken cancellationToken)
    {
        var venue = await GetOwnedVenue(venueId, cancellationToken);
        if (venue is null) return NotFound(new { message = "Không tìm thấy cụm sân." });
        foreach (var img in venue.VenueImages) img.IsPrimary = img.VenueImageId == imageId;
        await _venueRepository.SaveChangesAsync(cancellationToken);
        return Ok(MapVenue(venue));
    }

    public async Task<ServiceResult> DeleteVenue(int venueId, CancellationToken cancellationToken)
    {
        var venue = await GetOwnedVenue(venueId, cancellationToken);
        if (venue is null) return NotFound(new { message = "Không tìm thấy cụm sân." });
        venue.ApprovalStatus = "Deleted";
        await _venueRepository.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    public async Task<ServiceResult<OwnerCourtResponse>> CreateCourt(
        int venueId, OwnerCourtUpsertRequest request, CancellationToken cancellationToken)
    {
        return await AddCourt(venueId, new OwnerCourtCreateRequest
        {
            CourtType = request.CourtType,
            SurfaceType = request.SurfaceType,
            HourlyPrice = request.HourlyPrice,
            IsIndoor = request.IsIndoor
        }, cancellationToken);
    }

    public async Task<ServiceResult<OwnerCourtResponse>> UpdateCourt(
        int courtId, OwnerCourtUpsertRequest request, CancellationToken cancellationToken)
    {
        var court = await _venueRepository.Courts.SingleOrDefaultAsync(c => c.CourtId == courtId, cancellationToken);
        if (court is null) return NotFound(new { message = "Không tìm thấy sân con." });
        return await UpdateCourt(court.VenueId, courtId, new OwnerCourtUpdateRequest
        {
            CourtType = request.CourtType,
            SurfaceType = request.SurfaceType,
            HourlyPrice = request.HourlyPrice,
            IsIndoor = request.IsIndoor
        }, cancellationToken);
    }

    public async Task<ServiceResult> DeleteCourt(int courtId, CancellationToken cancellationToken)
    {
        var court = await _venueRepository.Courts.SingleOrDefaultAsync(c => c.CourtId == courtId, cancellationToken);
        if (court is null) return NotFound(new { message = "Không tìm thấy sân con." });
        return await DeleteCourt(court.VenueId, courtId, cancellationToken);
    }

    public async Task<ServiceResult<OwnerScheduleResponse>> GetScheduleV2(
        DateOnly date,
        string view = "day",
        CancellationToken cancellationToken = default)
    {
        var viewMode = view.Equals("week", StringComparison.OrdinalIgnoreCase) ? "week" : "day";
        var daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
        var startDate = viewMode == "week" ? date.AddDays(-daysSinceMonday) : date;
        var endDate = viewMode == "week" ? startDate.AddDays(6) : startDate;
        var rangeStart = startDate.ToDateTime(TimeOnly.MinValue);
        var rangeEnd = endDate.AddDays(1).ToDateTime(TimeOnly.MinValue);

        var response = new OwnerScheduleResponse
        {
            Date = date,
            StartDate = startDate,
            EndDate = endDate,
            View = viewMode,
            SlotMinutes = 30
        };

        var owner = await GetOwnerAsync(false, cancellationToken);
        if (owner is null) return Ok(response);

        var venues = await LoadOwnerVenues(owner.OwnerId, cancellationToken);
        response.Venues = venues.Select(MapVenue).ToList();

        var bookings = await _paymentRepository.Bookings
            .AsNoTracking()
            .AsSplitQuery()
            .Where(booking => booking.Court.Venue.OwnerId == owner.OwnerId
                && (booking.Slots.Any(slot => slot.StartTime < rangeEnd && slot.EndTime > rangeStart)
                    || (!booking.Slots.Any() && booking.StartTime < rangeEnd && booking.EndTime > rangeStart))
                && booking.Status != "Cancelled"
                && booking.Status != "Expired"
                && (booking.Status != "Holding" || booking.HoldExpiresAt > DateTime.UtcNow))
            .Include(booking => booking.Court).ThenInclude(court => court.Venue)
            .Include(booking => booking.Slots)
            .Include(booking => booking.CheckInGroups)
            .Include(booking => booking.Operation)
            .Include(booking => booking.Player).ThenInclude(player => player!.User)
            .OrderBy(booking => booking.StartTime)
            .ToListAsync(cancellationToken);

        var bookingIds = bookings.Select(booking => booking.BookingId).ToList();
        List<SchedulePayment> payments = bookingIds.Count == 0
            ? []
            : await _paymentRepository.Payments
                .AsNoTracking()
                .Where(payment => bookingIds.Contains(payment.BookingId))
                .OrderByDescending(payment => payment.PaymentId)
                .Select(payment => new SchedulePayment(payment.BookingId, payment.Status))
                .ToListAsync(cancellationToken);
        var latestPayments = payments
            .GroupBy(payment => payment.BookingId)
            .ToDictionary(group => group.Key, group => group.First());
        var paidBookingIds = payments
            .Where(payment => payment.Status == "Paid")
            .Select(payment => payment.BookingId)
            .ToHashSet();

        var localNow = VietnamTime.Now;
        response.Items = bookings.Select(booking => new OwnerScheduleItemResponse
        {
            BookingId = booking.BookingId,
            CourtId = booking.CourtId,
            VenueId = booking.Court.VenueId,
            VenueName = booking.Court.Venue.VenueName,
            CourtNumber = booking.Court.CourtNumber,
            StartTime = booking.StartTime,
            EndTime = booking.EndTime,
            Status = booking.Status,
            CustomerName = booking.Player?.User.Username,
            CustomerUserId = booking.Player?.UserId,
            Amount = booking.TotalAmount,
            PaymentStatus = latestPayments.GetValueOrDefault(booking.BookingId)?.Status,
            CheckInStatus = GetBookingCheckInStatus(booking, localNow),
            CanCancel = !paidBookingIds.Contains(booking.BookingId) && !HasStartedSlot(booking, localNow),
            IsOwnerBlock = booking.PlayerId is null && (booking.OwnerEntryType is null or "Blocked"),
            IsOwnerEntry = booking.PlayerId is null && booking.Status == "Blocked",
            EntryType = booking.OwnerEntryType ?? (booking.PlayerId is null ? "Blocked" : null),
            Title = booking.Title
        }).ToList();

        foreach (var venue in venues)
        {
            foreach (var court in venue.Courts.OrderBy(item => item.CourtNumber))
            {
                for (var slotDate = startDate; slotDate <= endDate; slotDate = slotDate.AddDays(1))
                {
                    var opening = slotDate.ToDateTime(venue.OpenTime);
                    var closing = slotDate.ToDateTime(venue.CloseTime);
                    for (var slotStart = opening; slotStart.AddMinutes(response.SlotMinutes) <= closing; slotStart = slotStart.AddMinutes(response.SlotMinutes))
                    {
                        var slotEnd = slotStart.AddMinutes(response.SlotMinutes);
                        var overlap = bookings.FirstOrDefault(booking =>
                            booking.Slots.Any(slot => slot.CourtId == court.CourtId && slot.StartTime < slotEnd && slot.EndTime > slotStart)
                            || (!booking.Slots.Any() && booking.CourtId == court.CourtId && booking.StartTime < slotEnd && booking.EndTime > slotStart));
                        var status = !venue.IsOpen
                            ? "Closed"
                            : court.AvailabilityStatus == "Inactive"
                                ? "Inactive"
                                : court.AvailabilityStatus == "Maintenance"
                                    ? "Maintenance"
                                    : overlap is null
                                        ? "Available"
                                        : overlap.PlayerId is not null
                                            ? overlap.Status == "Holding" ? "Holding" : "Booked"
                                            : overlap.OwnerEntryType ?? "Blocked";

                        response.Slots.Add(new OwnerScheduleSlotResponse
                        {
                            CourtId = court.CourtId,
                            VenueId = venue.VenueId,
                            VenueName = venue.VenueName,
                            CourtNumber = court.CourtNumber,
                            StartTime = slotStart,
                            EndTime = slotEnd,
                            Status = status,
                            BookingId = overlap?.BookingId,
                            CheckInStatus = overlap?.PlayerId is not null
                                ? GetSlotCheckInStatus(overlap, court.CourtId, slotStart, slotEnd, localNow)
                                : null,
                            EntryType = overlap?.OwnerEntryType,
                            Title = overlap?.Title
                        });
                    }
                }
            }
        }

        return Ok(response);
    }

    public Task<ServiceResult<OwnerScheduleResponse>> GetSchedule(DateOnly date, CancellationToken cancellationToken) =>
        GetScheduleV2(date, "day", cancellationToken);

    public Task<ServiceResult<OwnerScheduleItemResponse>> CreateScheduleEntry(OwnerScheduleBlockRequest request, CancellationToken cancellationToken) =>
        Task.FromResult<ServiceResult<OwnerScheduleItemResponse>>(Ok(new OwnerScheduleItemResponse()));

    public Task<ServiceResult<OwnerScheduleItemResponse>> CreateBlock(OwnerScheduleBlockRequest request, CancellationToken cancellationToken) =>
        Task.FromResult<ServiceResult<OwnerScheduleItemResponse>>(Ok(new OwnerScheduleItemResponse()));

    public Task<ServiceResult> DeleteScheduleEntry(int bookingId, CancellationToken cancellationToken) =>
        Task.FromResult(NoContent());

    public Task<ServiceResult> DeleteBlock(int bookingId, CancellationToken cancellationToken) =>
        Task.FromResult(NoContent());

    public Task<ServiceResult> UpdateBookingStatus(int bookingId, OwnerBookingStatusRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(Ok());

    public async Task<ServiceResult<OwnerBankAccountResponse>> GetBankAccount(CancellationToken cancellationToken)
    {
        var owner = await GetOwnerAsync(false, cancellationToken);
        if (owner is null) return NotFound(new { message = "Không tìm thấy tài khoản Owner." });

        var bankAccount = await _venueRepository.GetOwnerBankAccountAsync(owner.OwnerId, cancellationToken);
        return bankAccount is null
            ? NotFound(new { message = "Chưa cấu hình tài khoản ngân hàng." })
            : Ok(new OwnerBankAccountResponse
            {
                BankName = bankAccount.BankName,
                AccountNo = bankAccount.AccountNo,
                AccountHolderName = bankAccount.AccountHolderName
            });
    }

    public async Task<ServiceResult<OwnerBankAccountResponse>> UpsertBankAccount(
        OwnerBankAccountUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var owner = await GetOwnerAsync(true, cancellationToken);
        if (owner is null) return Unauthorized();

        var bankAccount = await _venueRepository.GetOwnerBankAccountAsync(owner.OwnerId, cancellationToken);
        if (bankAccount is null)
        {
            bankAccount = new OwnerBankAccount
            {
                OwnerId = owner.OwnerId,
                BankName = request.BankName.Trim(),
                AccountNo = request.AccountNo.Trim(),
                AccountHolderName = request.AccountHolderName.Trim(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            owner.OwnerBankAccounts.Add(bankAccount);
        }
        else
        {
            bankAccount.BankName = request.BankName.Trim();
            bankAccount.AccountNo = request.AccountNo.Trim();
            bankAccount.AccountHolderName = request.AccountHolderName.Trim();
            bankAccount.UpdatedAt = DateTime.UtcNow;
        }

        await _venueRepository.SaveChangesAsync(cancellationToken);
        return Ok(new OwnerBankAccountResponse
        {
            BankName = bankAccount.BankName,
            AccountNo = bankAccount.AccountNo,
            AccountHolderName = bankAccount.AccountHolderName
        });
    }

    public async Task<ServiceResult<OwnerCourtResponse>> AddCourt(
        int venueId,
        OwnerCourtCreateRequest request,
        CancellationToken cancellationToken)
    {
        var venue = await GetOwnedVenue(venueId, cancellationToken);
        if (venue is null) return NotFound(new { message = "Không tìm thấy cụm sân." });

        var nextCourtNumber = venue.Courts.Count == 0 ? 1 : venue.Courts.Max(c => c.CourtNumber) + 1;
        var basePrice = decimal.TryParse(venue.BookingRules.FirstOrDefault(rule => rule.RuleType == "BasePrice")?.RuleContent, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedPrice) ? parsedPrice : 0;
        var court = new Court
        {
            VenueId = venueId,
            CourtNumber = nextCourtNumber,
            CourtType = request.CourtType ?? "Standard",
            SurfaceType = Normalize(request.SurfaceType) ?? "Hard court",
            HourlyPrice = request.HourlyPrice ?? basePrice,
            IsIndoor = request.IsIndoor ?? false,
            AvailabilityStatus = "Available"
        };

        venue.Courts.Add(court);
        VenueApprovalWorkflow.MarkChangedByOwner(venue);
        AddAuditLog(venue, $"AddedCourt:{court.CourtNumber}");
        await _venueRepository.SaveChangesAsync(cancellationToken);

        _venueRealtime.Publish(venueId, "CourtAdded");
        return CreatedAtAction(nameof(GetVenue), new { venueId }, MapCourt(court));
    }

    public async Task<ServiceResult<OwnerCourtResponse>> UpdateCourt(
        int venueId,
        int courtId,
        OwnerCourtUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var venue = await GetOwnedVenue(venueId, cancellationToken);
        if (venue is null) return NotFound(new { message = "Không tìm thấy cụm sân." });

        var court = venue.Courts.FirstOrDefault(c => c.CourtId == courtId);
        if (court is null) return NotFound(new { message = "Không tìm thấy sân con." });

        if (request.CourtType is not null) court.CourtType = request.CourtType;
        if (request.SurfaceType is not null) court.SurfaceType = Normalize(request.SurfaceType);
        if (request.HourlyPrice.HasValue) court.HourlyPrice = request.HourlyPrice.Value;
        if (request.IsIndoor.HasValue) court.IsIndoor = request.IsIndoor.Value;
        if (request.AvailabilityStatus is not null) court.AvailabilityStatus = request.AvailabilityStatus;

        VenueApprovalWorkflow.MarkChangedByOwner(venue);
        AddAuditLog(venue, $"UpdatedCourt:{court.CourtId}");
        await _venueRepository.SaveChangesAsync(cancellationToken);

        _venueRealtime.Publish(venueId, "CourtUpdated");
        return Ok(MapCourt(court));
    }

    public async Task<ServiceResult> DeleteCourt(
        int venueId,
        int courtId,
        CancellationToken cancellationToken)
    {
        var venue = await GetOwnedVenue(venueId, cancellationToken);
        if (venue is null) return NotFound(new { message = "Không tìm thấy cụm sân." });

        var court = venue.Courts.FirstOrDefault(c => c.CourtId == courtId);
        if (court is null) return NotFound(new { message = "Không tìm thấy sân con." });

        if (await _venueRepository.CourtHasDependentsAsync(courtId, cancellationToken))
        {
            // Đã có lịch đặt/check-in/tỉ số → ẩn để giữ lịch sử (trạng thái Inactive đã bị lọc khắp nơi).
            court.AvailabilityStatus = "Inactive";
            AddAuditLog(venue, $"DeactivatedCourt:{court.CourtId}");
        }
        else
        {
            // Chưa có dữ liệu liên quan → xóa hẳn khỏi DB, giải phóng số sân, không để lại rác.
            _venueRepository.RemoveCourt(court);
            AddAuditLog(venue, $"DeletedCourt:{court.CourtId}");
        }

        VenueApprovalWorkflow.MarkChangedByOwner(venue);
        await _venueRepository.SaveChangesAsync(cancellationToken);

        _venueRealtime.Publish(venueId, "CourtDeleted");
        return NoContent();
    }

    public async Task<ServiceResult<List<OwnerVenueImageResponse>>> UploadVenueImages(
        int venueId,
        List<IFormFile> files,
        CancellationToken cancellationToken)
    {
        var venue = await GetOwnedVenue(venueId, cancellationToken);
        if (venue is null) return NotFound(new { message = "Không tìm thấy cụm sân." });
        if (files is null || files.Count == 0)
            return BadRequest(new { message = "Hãy chọn ít nhất 1 ảnh để tải lên." });

        var addedImages = new List<VenueImage>();
        var currentMaxSort = venue.VenueImages.Count == 0 ? -1 : venue.VenueImages.Max(i => i.SortOrder);

        foreach (var file in files)
        {
            if (file.Length > MaxVenueImageBytes) continue;
            var ext = Path.GetExtension(file.FileName);
            if (!AllowedImageExtensions.Contains(ext) && !AllowedImageContentTypes.Contains(file.ContentType)) continue;

            var fileName = $"venue-{venueId}-{Guid.NewGuid():N}{ext}";
            string imageUrl;
            try
            {
                await using var stream = file.OpenReadStream();
                imageUrl = await _cloudinaryUpload.UploadImageAsync(
                    stream,
                    fileName,
                    "picklink_venues",
                    cancellationToken);
            }
            catch
            {
                continue;
            }

            currentMaxSort++;
            var image = new VenueImage
            {
                VenueId = venueId,
                ImageUrl = imageUrl,
                Caption = file.FileName,
                IsPrimary = venue.VenueImages.Count == 0 && addedImages.Count == 0,
                SortOrder = currentMaxSort
            };

            venue.VenueImages.Add(image);
            addedImages.Add(image);
        }

        if (addedImages.Count == 0)
            return BadRequest(new { message = "Không có ảnh hợp lệ nào được tải lên." });

        VenueApprovalWorkflow.MarkChangedByOwner(venue);
        AddAuditLog(venue, $"UploadedImages:{addedImages.Count}");
        await _venueRepository.SaveChangesAsync(cancellationToken);

        _venueRealtime.Publish(venueId, "ImagesUploaded");
        return Ok(addedImages.Select(MapImage).ToList());
    }

    public async Task<ServiceResult> DeleteVenueImage(
        int venueId,
        int imageId,
        CancellationToken cancellationToken)
    {
        var venue = await GetOwnedVenue(venueId, cancellationToken);
        if (venue is null) return NotFound(new { message = "Không tìm thấy cụm sân." });

        var image = venue.VenueImages.FirstOrDefault(i => i.VenueImageId == imageId);
        if (image is null) return NotFound(new { message = "Không tìm thấy ảnh." });

        TryDeleteVenueImage(image.ImageUrl);
        venue.VenueImages.Remove(image);

        if (image.IsPrimary && venue.VenueImages.Count > 0)
        {
            var first = venue.VenueImages.OrderBy(i => i.SortOrder).First();
            first.IsPrimary = true;
        }

        VenueApprovalWorkflow.MarkChangedByOwner(venue);
        AddAuditLog(venue, $"DeletedImage:{imageId}");
        await _venueRepository.SaveChangesAsync(cancellationToken);

        _venueRealtime.Publish(venueId, "ImageDeleted");
        return NoContent();
    }

    private async Task<VenueOwner?> GetOwnerAsync(bool createIfMissing, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return null;

        // OwnerBankAccounts is a plain alias property, not a mapped navigation, so EF cannot
        // Include it; the navigation itself is BankAccounts.
        var owner = await _venueRepository.VenueOwners
            .AsSingleQuery()
            .Include(o => o.BankAccounts)
            .SingleOrDefaultAsync(o => o.UserId == userId.Value, cancellationToken);

        if (owner is null && createIfMissing)
        {
            owner = new VenueOwner
            {
                UserId = userId.Value
            };
            await _venueRepository.AddVenueOwnerAsync(owner, cancellationToken);
            await _venueRepository.SaveChangesAsync(cancellationToken);
        }

        return owner;
    }

    private async Task<List<Venue>> LoadOwnerVenues(int ownerId, CancellationToken cancellationToken)
    {
        // DB dùng chung đặt ở xa (~220ms/round-trip), nên gộp về 1 truy vấn (single query) thay vì
        // split thành nhiều round-trip sẽ nhanh hơn nhiều dù có lặp dữ liệu khi join.
        return await _venueRepository.Venues.AsNoTracking()
            .AsSingleQuery()
            .Include(v => v.Courts)
            .Include(v => v.VenueImages)
            .Include(v => v.Amenities)
            .Include(v => v.BookingRules)
            .Include(v => v.VenueListingPayments)
            .Where(v => v.OwnerId == ownerId && v.ApprovalStatus != "Deleted")
            .OrderBy(v => v.VenueName)
            .ToListAsync(cancellationToken);
    }

    private async Task<Venue?> GetOwnedVenue(int venueId, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return null;

        return await _venueRepository.Venues
            .AsSingleQuery()
            .Include(v => v.Courts)
            .Include(v => v.VenueImages)
            .Include(v => v.Amenities)
            .Include(v => v.BookingRules)
            .Include(v => v.VenueListingPayments)
            .SingleOrDefaultAsync(v => v.VenueId == venueId && v.Owner.UserId == userId.Value, cancellationToken);
    }

    private int? CurrentUserId() => _currentUserId;

    private static bool HasStartedSlot(Booking booking, DateTime localNow)
    {
        if (booking.Slots.Count > 0)
            return booking.Slots.Any(slot => localNow >= slot.StartTime);

        return localNow >= booking.StartTime;
    }

    private static string GetBookingCheckInStatus(Booking booking, DateTime localNow) =>
        BookingOccurrencePolicy.GetCheckInStatus(
            booking.Status,
            booking.Operation?.CheckInStatus,
            booking.CheckInGroups.Select(group => new BookingOccurrence(group.StartTime, group.EndTime, group.CheckInStatus)),
            localNow,
            booking.StartTime,
            booking.EndTime);

    private static string GetSlotCheckInStatus(
        Booking booking,
        int courtId,
        DateTime slotStart,
        DateTime slotEnd,
        DateTime localNow)
    {
        var group = booking.CheckInGroups.FirstOrDefault(item =>
            item.CourtId == courtId && item.StartTime < slotEnd && item.EndTime > slotStart);
        return group is null
            ? GetStoredCheckInStatus(booking.Operation?.CheckInStatus, booking.Status, booking.StartTime, localNow)
            : GetStoredCheckInStatus(group.CheckInStatus, booking.Status, group.StartTime, localNow);
    }

    private static string GetStoredCheckInStatus(
        string? storedStatus,
        string bookingStatus,
        DateTime startTime,
        DateTime localNow)
    {
        if (bookingStatus is "Cancelled" or "Expired") return "Cancelled";
        if (!string.IsNullOrWhiteSpace(storedStatus) && storedStatus != "Ready") return storedStatus;
        return bookingStatus == "Confirmed" && localNow >= startTime.AddMinutes(-30) ? "Ready" : "NotOpen";
    }

    private static void ApplyVenueDetails(Venue venue, OwnerVenueUpsertRequest request)
    {
        venue.Amenities.Clear();
        var amenities = (request.Amenities ?? new List<string>())
            .Select(Normalize)
            .Where(value => value is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(value => new Amenity { VenueId = venue.VenueId, AmenityName = value!, IsFree = true }).ToList();

        foreach (var amenity in amenities)
        {
            venue.Amenities.Add(amenity);
        }

        var priceRule = venue.BookingRules.FirstOrDefault(rule => rule.RuleType == "BasePrice");
        if (priceRule is null)
        {
            priceRule = new BookingRule { VenueId = venue.VenueId, RuleType = "BasePrice" };
            venue.BookingRules.Add(priceRule);
        }
        priceRule.RuleContent = request.BasePrice.ToString(CultureInfo.InvariantCulture);
    }

    private static OwnerVenueResponse MapVenue(Venue venue)
    {
        var now = DateTime.UtcNow;
        var latestPayment = venue.VenueListingPayments
            .OrderByDescending(payment => payment.SubmittedAt)
            .FirstOrDefault();
        var activePaidUntil = venue.VenueListingPayments
            .Where(payment => payment.Status == "Confirmed" && payment.PaidUntil >= now)
            .OrderByDescending(payment => payment.PaidUntil)
            .Select(payment => payment.PaidUntil)
            .FirstOrDefault();
        var listingStatus = activePaidUntil.HasValue
            ? "Paid"
            : latestPayment?.Status == "PendingReview"
                ? "PendingReview"
                : latestPayment?.Status == "Rejected"
                    ? "Rejected"
                    : venue.VenueListingPayments.Any(payment => payment.Status == "Confirmed")
                        ? "Expired"
                        : "Unpaid";

        return new OwnerVenueResponse
        {
            VenueId = venue.VenueId,
            VenueName = venue.VenueName,
            Address = venue.Address,
            OverallRating = venue.OverallRating,
            OpenTime = venue.OpenTime,
            CloseTime = venue.CloseTime,
            PhoneNumber = venue.PhoneNumber,
            Latitude = venue.Latitude,
            Longitude = venue.Longitude,
            IsOpen = venue.IsOpen,
            ApprovalStatus = venue.ApprovalStatus,
            RejectionReason = venue.RejectionReason,
            ListingStatus = listingStatus,
            ListingExpiresAt = activePaidUntil,
            LatestListingPayment = latestPayment is null ? null : MapListingPayment(latestPayment),
            BasePrice = decimal.TryParse(venue.BookingRules.FirstOrDefault(rule => rule.RuleType == "BasePrice")?.RuleContent, NumberStyles.Any, CultureInfo.InvariantCulture, out var price) ? price : 0,
            Amenities = venue.Amenities.Select(item => item.AmenityName).ToList(),
            Images = venue.VenueImages.OrderByDescending(image => image.IsPrimary).ThenBy(image => image.SortOrder).Select(MapImage).ToList(),
            Courts = venue.Courts.Where(court => court.AvailabilityStatus != "Inactive").OrderBy(court => court.CourtNumber).Select(MapCourt).ToList()
        };
    }

    private static OwnerVenueReviewResponse MapOwnerReview(RatingHistory review) => new()
    {
        RatingId = review.RatingId,
        BookingId = review.BookingId,
        ReviewerName = review.IsAnonymous ? "Ẩn danh" : review.User.Username,
        CourtNumber = review.Booking?.Court?.CourtNumber,
        Score = review.Score,
        Comment = review.Comment,
        Tags = ParseReviewTags(review.Tags),
        IsAnonymous = review.IsAnonymous,
        CreatedAt = review.CreatedAt
    };

    private static List<string> ParseReviewTags(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private Task<decimal> GetCurrentListingPriceAsync(CancellationToken cancellationToken) =>
        _paymentRepository.GetCurrentListingPriceAsync(cancellationToken);

    private static int ActiveCourtCount(Venue venue) =>
        venue.Courts.Count(court => court.AvailabilityStatus != "Inactive");

    private static OwnerListingFeePaymentResponse MapListingPayment(VenueListingPayment payment) => new()
    {
        VenueListingPaymentId = payment.VenueListingPaymentId,
        VenueId = payment.VenueId,
        Months = payment.Months,
        ActiveCourtCount = payment.ActiveCourtCount,
        PricePerCourtPerMonth = payment.PricePerCourtPerMonth,
        Amount = payment.Amount,
        Status = payment.Status,
        ReceiptImageUrl = payment.ReceiptImageUrl,
        RejectionReason = payment.RejectionReason,
        SubmittedAt = payment.SubmittedAt,
        PaidFrom = payment.PaidFrom,
        PaidUntil = payment.PaidUntil
    };

    private static OwnerCourtResponse MapCourt(Court court) => new()
    {
        CourtId = court.CourtId,
        VenueId = court.VenueId,
        CourtNumber = court.CourtNumber,
        CourtType = court.CourtType ?? "Standard",
        SurfaceType = court.SurfaceType,
        HourlyPrice = court.HourlyPrice,
        IsIndoor = court.IsIndoor,
        AvailabilityStatus = court.AvailabilityStatus
    };

    private static OwnerVenueImageResponse MapImage(VenueImage image) => new()
    {
        VenueImageId = image.VenueImageId,
        ImageUrl = image.ImageUrl,
        Caption = image.Caption,
        IsPrimary = image.IsPrimary,
        SortOrder = image.SortOrder
    };

    private void AddAuditLog(Venue venue, string action)
    {
        var userId = CurrentUserId();
        if (userId is null) return;
        venue.VenueAuditLogs.Add(new VenueAuditLog
        {
            VenueId = venue.VenueId,
            ActorId = userId.Value,
            Action = action,
            Timestamp = DateTime.UtcNow
        });
    }

    private async Task<string> SaveListingFeeReceiptAsync(
        int paymentId,
        IFormFile receipt,
        CancellationToken cancellationToken)
    {
        var extension = receipt.ContentType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".jpg"
        };
        var fileName = $"listing-fee-{paymentId}-{Guid.NewGuid():N}{extension}";
        await using var stream = receipt.OpenReadStream();
        return await _cloudinaryUpload.UploadImageAsync(
            stream,
            fileName,
            "picklink_receipts",
            cancellationToken);
    }

    private string PublicUrl(string relativeUrl)
    {
        var publicBaseUrl = _configuration["PublicBaseUrl"]?.TrimEnd('/');
        return string.IsNullOrWhiteSpace(publicBaseUrl) ? relativeUrl : $"{publicBaseUrl}{relativeUrl}";
    }

    private void TryDeleteVenueImage(string imageUrl)
    {
        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri)) return;
        var marker = "/uploads/venues/";
        var index = uri.AbsolutePath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return;
        var relativePath = Uri.UnescapeDataString(uri.AbsolutePath[(index + 1)..]).Replace('/', Path.DirectorySeparatorChar);
        var webRoot = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        var fullPath = Path.GetFullPath(Path.Combine(webRoot, relativePath));
        var venueRoot = Path.GetFullPath(Path.Combine(webRoot, "uploads", "venues")) + Path.DirectorySeparatorChar;
        if (fullPath.StartsWith(venueRoot, StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(fullPath))
            System.IO.File.Delete(fullPath);
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
