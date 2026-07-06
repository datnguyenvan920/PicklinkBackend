using System.Globalization;
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
[Route("api/owner")]
public class OwnerVenueController : ControllerBase
{
    private const long MaxVenueImageBytes = 5 * 1024 * 1024;
    private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };
    private static readonly HashSet<string> AllowedReceiptTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp"
    };
    private readonly ApplicationDbContext _dbContext;
    private readonly IWebHostEnvironment _environment;
    private readonly ScheduleRealtimeNotifier _scheduleRealtime;
    private readonly VenueRealtimeNotifier _venueRealtime;

    public OwnerVenueController(
        ApplicationDbContext dbContext,
        IWebHostEnvironment environment,
        ScheduleRealtimeNotifier scheduleRealtime,
        VenueRealtimeNotifier venueRealtime)
    {
        _dbContext = dbContext;
        _environment = environment;
        _scheduleRealtime = scheduleRealtime;
        _venueRealtime = venueRealtime;
    }

    [HttpGet("venues")]
    public async Task<ActionResult<List<OwnerVenueResponse>>> GetVenues(CancellationToken cancellationToken)
    {
        var owner = await GetOwnerAsync(false, cancellationToken);
        if (owner is null) return Ok(new List<OwnerVenueResponse>());

        var venues = await LoadOwnerVenues(owner.OwnerId, cancellationToken);
        return Ok(venues.Select(MapVenue).ToList());
    }

    [HttpGet("venues/{venueId:int}")]
    public async Task<ActionResult<OwnerVenueResponse>> GetVenue(int venueId, CancellationToken cancellationToken)
    {
        var venue = await GetOwnedVenue(venueId, cancellationToken);
        return venue is null ? NotFound(new { message = "Không tìm thấy cụm sân." }) : Ok(MapVenue(venue));
    }

    [HttpPost("venues")]
    public async Task<ActionResult<OwnerVenueResponse>> CreateVenue(
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

        _dbContext.Venues.Add(venue);
        await _dbContext.SaveChangesAsync(cancellationToken);
        ApplyVenueDetails(venue, request);

        for (var number = 1; number <= request.InitialCourtCount; number++)
        {
            _dbContext.Courts.Add(new Court
            {
                VenueId = venue.VenueId,
                CourtNumber = number,
                CourtType = "Standard",
                SurfaceType = "Hard court",
                HourlyPrice = request.BasePrice,
                AvailabilityStatus = "Available"
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _venueRealtime.Publish(venue.VenueId, "Created");
        return CreatedAtAction(nameof(GetVenue), new { venueId = venue.VenueId }, MapVenue(venue));
    }

    [HttpPut("venues/{venueId:int}")]
    public async Task<ActionResult<OwnerVenueResponse>> UpdateVenue(
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
        MarkVenueChanged(venue);
        ApplyVenueDetails(venue, request);

        await _dbContext.SaveChangesAsync(cancellationToken);
        _venueRealtime.Publish(venueId, "Updated");
        return Ok(MapVenue(venue));
    }

    [HttpPatch("venues/{venueId:int}/open-status")]
    public async Task<ActionResult<OwnerVenueResponse>> SetVenueOpenStatus(
        int venueId,
        OwnerVenueOpenStatusRequest request,
        CancellationToken cancellationToken)
    {
        var venue = await GetOwnedVenue(venueId, cancellationToken);
        if (venue is null) return NotFound(new { message = "Không tìm thấy cụm sân." });

        venue.IsOpen = request.IsOpen;
        AddAuditLog(venue, request.IsOpen ? "OwnerOpenedVenue" : "OwnerClosedVenue");
        await _dbContext.SaveChangesAsync(cancellationToken);
        _venueRealtime.Publish(venueId, request.IsOpen ? "Opened" : "Closed");
        return Ok(MapVenue(venue));
    }

    [HttpPost("venues/{venueId:int}/submit")]
    public async Task<ActionResult<OwnerVenueResponse>> SubmitVenueForApproval(
        int venueId,
        CancellationToken cancellationToken)
    {
        var venue = await GetOwnedVenue(venueId, cancellationToken);
        if (venue is null) return NotFound(new { message = "Không tìm thấy cụm sân." });
        if (venue.ApprovalStatus == "Pending")
            return Conflict(new { message = "Cụm sân đang chờ Admin duyệt." });
        if (venue.Courts.Count == 0)
            return BadRequest(new { message = "Hãy thêm ít nhất một sân con trước khi gửi duyệt." });
        if (venue.Latitude is null || venue.Longitude is null)
            return BadRequest(new { message = "Hãy định vị cụm sân trên bản đồ trước khi gửi duyệt." });

        venue.ApprovalStatus = "Pending";
        venue.RejectionReason = null;
        AddAuditLog(venue, "OwnerSubmittedForApproval");
        await _dbContext.SaveChangesAsync(cancellationToken);
        _venueRealtime.Publish(venueId, "Submitted");
        return Ok(MapVenue(venue));
    }

    [HttpGet("venues/{venueId:int}/listing-fee/preview")]
    public async Task<ActionResult<OwnerListingFeePreviewResponse>> PreviewListingFee(
        int venueId,
        int months = 1,
        CancellationToken cancellationToken = default)
    {
        if (months is < 1 or > 24)
            return BadRequest(new { message = "Số tháng phải từ 1 đến 24." });

        var venue = await GetOwnedVenue(venueId, cancellationToken);
        if (venue is null) return NotFound(new { message = "Không tìm thấy cụm sân." });

        var activeCourtCount = ActiveCourtCount(venue);
        if (activeCourtCount == 0)
            return BadRequest(new { message = "Cụm sân cần ít nhất một sân con đang hoạt động để tính phí lên sàn." });

        var price = await GetCurrentListingPriceAsync(cancellationToken);
        if (price <= 0)
            return Conflict(new { message = "Admin chưa cấu hình đơn giá phí lên sàn." });

        return Ok(new OwnerListingFeePreviewResponse
        {
            VenueId = venueId,
            Months = months,
            ActiveCourtCount = activeCourtCount,
            PricePerCourtPerMonth = price,
            Amount = activeCourtCount * price * months
        });
    }

    [HttpPost("venues/{venueId:int}/listing-fee/payments")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 8 * 1024 * 1024)]
    public async Task<ActionResult<OwnerListingFeePaymentResponse>> SubmitListingFeePayment(
        int venueId,
        [FromForm] OwnerListingFeePaymentRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Months is < 1 or > 24)
            return BadRequest(new { message = "Số tháng phải từ 1 đến 24." });
        if (request.Receipt is null || request.Receipt.Length == 0)
            return BadRequest(new { message = "Vui lòng tải ảnh biên lai." });
        if (request.Receipt.Length > 5 * 1024 * 1024)
            return BadRequest(new { message = "Ảnh biên lai không được vượt quá 5 MB." });
        if (!AllowedReceiptTypes.Contains(request.Receipt.ContentType))
            return BadRequest(new { message = "Biên lai chỉ hỗ trợ JPG, PNG hoặc WEBP." });

        var venue = await GetOwnedVenue(venueId, cancellationToken);
        if (venue is null) return NotFound(new { message = "Không tìm thấy cụm sân." });

        var activeCourtCount = ActiveCourtCount(venue);
        if (activeCourtCount == 0)
            return BadRequest(new { message = "Cụm sân cần ít nhất một sân con đang hoạt động để tính phí lên sàn." });

        var price = await GetCurrentListingPriceAsync(cancellationToken);
        if (price <= 0)
            return Conflict(new { message = "Admin chưa cấu hình đơn giá phí lên sàn." });

        var payment = new VenueListingPayment
        {
            VenueId = venueId,
            Months = request.Months,
            ActiveCourtCount = activeCourtCount,
            PricePerCourtPerMonth = price,
            Amount = activeCourtCount * price * request.Months,
            Status = "PendingReview",
            SubmittedAt = DateTime.UtcNow
        };
        _dbContext.VenueListingPayments.Add(payment);
        await _dbContext.SaveChangesAsync(cancellationToken);

        payment.ReceiptImageUrl = await SaveListingFeeReceiptAsync(payment.VenueListingPaymentId, request.Receipt, cancellationToken);
        AddAuditLog(venue, $"ListingFeeSubmitted:{payment.VenueListingPaymentId}");
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(MapListingPayment(payment));
    }

    [HttpPost("venues/{venueId:int}/images")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxVenueImageBytes + 1024 * 100)]
    public async Task<ActionResult<OwnerVenueImageResponse>> UploadVenueImage(
        int venueId,
        [FromForm] OwnerVenueImageUploadRequest request,
        CancellationToken cancellationToken)
    {
        var venue = await GetOwnedVenue(venueId, cancellationToken);
        if (venue is null) return NotFound(new { message = "Không tìm thấy cụm sân." });
        if (venue.VenueImages.Count >= 10)
            return BadRequest(new { message = "Mỗi cụm sân được tải tối đa 10 ảnh." });
        if (request.Image.Length == 0 || request.Image.Length > MaxVenueImageBytes)
            return BadRequest(new { message = "Ảnh sân phải có dung lượng từ 1 byte đến 5MB." });

        var extension = Path.GetExtension(request.Image.FileName);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedImageExtensions.Contains(extension))
            return BadRequest(new { message = "Chỉ hỗ trợ ảnh JPG, PNG hoặc WEBP." });

        var webRoot = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        var directory = Path.Combine(webRoot, "uploads", "venues", venueId.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);
        var fileName = $"venue-{venueId}-{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        await using (var stream = System.IO.File.Create(Path.Combine(directory, fileName)))
        {
            await request.Image.CopyToAsync(stream, cancellationToken);
        }

        var image = new VenueImage
        {
            VenueId = venueId,
            ImageUrl = $"{Request.Scheme}://{Request.Host}/uploads/venues/{venueId}/{fileName}",
            Caption = Normalize(request.Caption),
            IsPrimary = venue.VenueImages.Count == 0,
            SortOrder = venue.VenueImages.Count,
            CreatedAt = DateTime.UtcNow
        };
        venue.VenueImages.Add(image);
        MarkVenueChanged(venue);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _venueRealtime.Publish(venueId, "ImageAdded");
        return Ok(MapImage(image));
    }

    [HttpPatch("venues/{venueId:int}/images/{imageId:int}/primary")]
    public async Task<ActionResult<OwnerVenueResponse>> SetPrimaryVenueImage(
        int venueId,
        int imageId,
        CancellationToken cancellationToken)
    {
        var venue = await GetOwnedVenue(venueId, cancellationToken);
        if (venue is null) return NotFound(new { message = "Không tìm thấy cụm sân." });
        if (venue.VenueImages.All(image => image.VenueImageId != imageId))
            return NotFound(new { message = "Không tìm thấy ảnh sân." });

        foreach (var image in venue.VenueImages) image.IsPrimary = image.VenueImageId == imageId;
        MarkVenueChanged(venue);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _venueRealtime.Publish(venueId, "PrimaryImageChanged");
        return Ok(MapVenue(venue));
    }

    [HttpDelete("venues/{venueId:int}/images/{imageId:int}")]
    public async Task<ActionResult> DeleteVenueImage(
        int venueId,
        int imageId,
        CancellationToken cancellationToken)
    {
        var venue = await GetOwnedVenue(venueId, cancellationToken);
        if (venue is null) return NotFound(new { message = "Không tìm thấy cụm sân." });
        var image = venue.VenueImages.FirstOrDefault(item => item.VenueImageId == imageId);
        if (image is null) return NotFound(new { message = "Không tìm thấy ảnh sân." });

        var wasPrimary = image.IsPrimary;
        _dbContext.VenueImages.Remove(image);
        venue.VenueImages.Remove(image);
        if (wasPrimary && venue.VenueImages.Count > 0)
            venue.VenueImages.OrderBy(item => item.SortOrder).First().IsPrimary = true;
        MarkVenueChanged(venue);
        await _dbContext.SaveChangesAsync(cancellationToken);
        TryDeleteVenueImage(image.ImageUrl);
        _venueRealtime.Publish(venueId, "ImageDeleted");
        return NoContent();
    }

    [HttpDelete("venues/{venueId:int}")]
    public async Task<ActionResult> DeleteVenue(int venueId, CancellationToken cancellationToken)
    {
        var venue = await GetOwnedVenue(venueId, cancellationToken);
        if (venue is null) return NotFound(new { message = "Không tìm thấy cụm sân." });

        if (venue.Courts.Any(court => court.Bookings.Count > 0))
            return Conflict(new { message = "Không thể xóa cụm sân đã có lịch đặt." });

        foreach (var image in venue.VenueImages) TryDeleteVenueImage(image.ImageUrl);
        _dbContext.Amenities.RemoveRange(venue.Amenities);
        _dbContext.BookingRules.RemoveRange(venue.BookingRules);
        _dbContext.VenueAuditLogs.RemoveRange(venue.VenueAuditLogs);
        _dbContext.Courts.RemoveRange(venue.Courts);
        _dbContext.Venues.Remove(venue);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _venueRealtime.Publish(venueId, "Deleted");
        return NoContent();
    }

    [HttpPost("venues/{venueId:int}/courts")]
    public async Task<ActionResult<OwnerCourtResponse>> CreateCourt(
        int venueId,
        OwnerCourtUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var venue = await GetOwnedVenue(venueId, cancellationToken);
        if (venue is null) return NotFound(new { message = "Không tìm thấy cụm sân." });
        if (venue.Courts.Any(court => court.CourtNumber == request.CourtNumber))
            return Conflict(new { message = "Số sân con đã tồn tại trong cụm sân này." });

        var court = new Court
        {
            VenueId = venueId,
            CourtNumber = request.CourtNumber,
            CourtType = request.CourtType.Trim(),
            SurfaceType = Normalize(request.SurfaceType),
            HourlyPrice = request.HourlyPrice,
            IsIndoor = request.IsIndoor,
            AvailabilityStatus = request.AvailabilityStatus
        };
        _dbContext.Courts.Add(court);
        MarkVenueChanged(venue);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _venueRealtime.Publish(venueId, "CourtCreated");
        return Ok(MapCourt(court));
    }

    [HttpPut("courts/{courtId:int}")]
    public async Task<ActionResult<OwnerCourtResponse>> UpdateCourt(
        int courtId,
        OwnerCourtUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var court = await GetOwnedCourt(courtId, cancellationToken);
        if (court is null) return NotFound(new { message = "Không tìm thấy sân con." });
        if (await _dbContext.Courts.AnyAsync(item => item.VenueId == court.VenueId && item.CourtId != courtId && item.CourtNumber == request.CourtNumber, cancellationToken))
            return Conflict(new { message = "Số sân con đã tồn tại trong cụm sân này." });

        court.CourtNumber = request.CourtNumber;
        court.CourtType = request.CourtType.Trim();
        court.SurfaceType = Normalize(request.SurfaceType);
        court.HourlyPrice = request.HourlyPrice;
        court.IsIndoor = request.IsIndoor;
        court.AvailabilityStatus = request.AvailabilityStatus;
        MarkVenueChanged(court.Venue);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _venueRealtime.Publish(court.VenueId, "CourtUpdated");
        return Ok(MapCourt(court));
    }

    [HttpDelete("courts/{courtId:int}")]
    public async Task<ActionResult> DeleteCourt(int courtId, CancellationToken cancellationToken)
    {
        var court = await GetOwnedCourt(courtId, cancellationToken);
        if (court is null) return NotFound(new { message = "Không tìm thấy sân con." });
        if (await _dbContext.Bookings.AnyAsync(booking => booking.CourtId == courtId, cancellationToken))
            return Conflict(new { message = "Không thể xóa sân con đã có lịch đặt." });

        var venueId = court.VenueId;
        MarkVenueChanged(court.Venue);
        _dbContext.Courts.Remove(court);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _venueRealtime.Publish(venueId, "CourtDeleted");
        return NoContent();
    }

    [HttpGet("schedule")]
    public async Task<ActionResult<OwnerScheduleResponse>> GetScheduleV2(
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

        var owner = await GetOwnerAsync(false, cancellationToken);
        var response = new OwnerScheduleResponse
        {
            Date = date,
            StartDate = startDate,
            EndDate = endDate,
            View = viewMode,
            SlotMinutes = 30
        };
        if (owner is null) return Ok(response);

        var venues = await LoadOwnerVenues(owner.OwnerId, cancellationToken);
        response.Venues = venues.Select(MapVenue).ToList();

        var bookings = await _dbContext.Bookings
            .AsNoTracking()
            .Where(booking => booking.Court.Venue.OwnerId == owner.OwnerId && booking.StartTime < rangeEnd && booking.EndTime > rangeStart && booking.Status != "Cancelled" && booking.Status != "Expired")
            .Include(booking => booking.Court).ThenInclude(court => court.Venue)
            .Include(booking => booking.Player).ThenInclude(player => player!.User)
            .Include(booking => booking.Payments)
            .OrderBy(booking => booking.StartTime)
            .ToListAsync(cancellationToken);

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
            Amount = booking.Payments.OrderByDescending(payment => payment.PaymentId).Select(payment => payment.Amount).FirstOrDefault(),
            PaymentStatus = booking.Payments.OrderByDescending(payment => payment.PaymentId).Select(payment => payment.Status).FirstOrDefault(),
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
                    for (var slotStart = opening; slotStart.AddMinutes(30) <= closing; slotStart = slotStart.AddMinutes(30))
                    {
                        var slotEnd = slotStart.AddMinutes(30);
                        var overlap = bookings.FirstOrDefault(item => item.CourtId == court.CourtId && item.StartTime < slotEnd && item.EndTime > slotStart);
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
                            EntryType = overlap?.OwnerEntryType,
                            Title = overlap?.Title
                        });
                    }
                }
            }
        }

        return Ok(response);
    }

    [HttpGet("schedule/legacy")]
    public async Task<ActionResult<OwnerScheduleResponse>> GetSchedule(DateOnly date, CancellationToken cancellationToken)
    {
        var owner = await GetOwnerAsync(false, cancellationToken);
        var response = new OwnerScheduleResponse { Date = date };
        if (owner is null) return Ok(response);

        var venues = await LoadOwnerVenues(owner.OwnerId, cancellationToken);
        response.Venues = venues.Select(MapVenue).ToList();

        var dayStart = date.ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);
        response.Items = await _dbContext.Bookings
            .AsNoTracking()
            .Where(booking => booking.Court.Venue.OwnerId == owner.OwnerId && booking.StartTime < dayEnd && booking.EndTime > dayStart && booking.Status != "Cancelled" && booking.Status != "Expired")
            .OrderBy(booking => booking.StartTime)
            .Select(booking => new OwnerScheduleItemResponse
            {
                BookingId = booking.BookingId,
                CourtId = booking.CourtId,
                VenueId = booking.Court.VenueId,
                VenueName = booking.Court.Venue.VenueName,
                CourtNumber = booking.Court.CourtNumber,
                StartTime = booking.StartTime,
                EndTime = booking.EndTime,
                Status = booking.Status,
                CustomerName = booking.Player == null ? null : booking.Player.User.Username,
                Amount = booking.Payments.OrderByDescending(payment => payment.PaymentId).Select(payment => payment.Amount).FirstOrDefault(),
                PaymentStatus = booking.Payments.OrderByDescending(payment => payment.PaymentId).Select(payment => payment.Status).FirstOrDefault(),
                IsOwnerBlock = booking.PlayerId == null && booking.Status == "Blocked"
            })
            .ToListAsync(cancellationToken);

        return Ok(response);
    }

    [HttpPost("schedule/entries")]
    public async Task<ActionResult<OwnerScheduleItemResponse>> CreateScheduleEntry(
        OwnerScheduleBlockRequest request,
        CancellationToken cancellationToken)
    {
        var court = await GetOwnedCourt(request.CourtId, cancellationToken);
        if (court is null) return NotFound(new { message = "Không tìm thấy sân con." });
        if (request.EndTime <= request.StartTime)
            return BadRequest(new { message = "Giờ kết thúc phải sau giờ bắt đầu." });
        if (DateOnly.FromDateTime(request.StartTime) != DateOnly.FromDateTime(request.EndTime))
            return BadRequest(new { message = "Khung lịch phải bắt đầu và kết thúc trong cùng một ngày." });
        if (request.StartTime.Minute % 30 != 0 || request.EndTime.Minute % 30 != 0 || request.StartTime.Second != 0 || request.EndTime.Second != 0 || (request.EndTime - request.StartTime).TotalMinutes % 30 != 0)
            return BadRequest(new { message = "Thời gian phải theo bước 30 phút." });

        var slotDate = DateOnly.FromDateTime(request.StartTime);
        var opening = slotDate.ToDateTime(court.Venue.OpenTime);
        var closing = slotDate.ToDateTime(court.Venue.CloseTime);
        if (request.StartTime < opening || request.EndTime > closing)
            return BadRequest(new { message = $"Khung giờ phải nằm trong giờ mở cửa {court.Venue.OpenTime:HH:mm}–{court.Venue.CloseTime:HH:mm}." });
        if (request.EntryType == "Event" && string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new { message = "Vui lòng nhập tên sự kiện." });

        var overlaps = await _dbContext.Bookings.AnyAsync(booking =>
            booking.CourtId == request.CourtId && booking.Status != "Cancelled" &&
            booking.StartTime < request.EndTime && booking.EndTime > request.StartTime,
            cancellationToken);
        if (overlaps) return Conflict(new { message = "Khung giờ đã có booking hoặc lịch vận hành khác." });

        var entryType = request.EntryType;
        var booking = new Booking
        {
            CourtId = request.CourtId,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            Status = "Blocked",
            CreatedAt = DateTime.UtcNow,
            OwnerEntryType = entryType,
            Title = Normalize(request.Title) ?? (entryType == "Maintenance" ? "Bảo trì sân" : entryType == "Event" ? "Sự kiện" : "Khóa bởi chủ sân")
        };
        _dbContext.Bookings.Add(booking);
        await _dbContext.SaveChangesAsync(cancellationToken);
        PublishScheduleChange(court, booking, "Created");

        return Ok(new OwnerScheduleItemResponse
        {
            BookingId = booking.BookingId,
            CourtId = court.CourtId,
            VenueId = court.VenueId,
            VenueName = court.Venue.VenueName,
            CourtNumber = court.CourtNumber,
            StartTime = booking.StartTime,
            EndTime = booking.EndTime,
            Status = booking.Status,
            IsOwnerBlock = entryType == "Blocked",
            IsOwnerEntry = true,
            EntryType = entryType,
            Title = booking.Title
        });
    }

    [HttpPost("schedule/blocks")]
    public async Task<ActionResult<OwnerScheduleItemResponse>> CreateBlock(
        OwnerScheduleBlockRequest request,
        CancellationToken cancellationToken)
    {
        var court = await GetOwnedCourt(request.CourtId, cancellationToken);
        if (court is null) return NotFound(new { message = "Không tìm thấy sân con." });
        if (request.EndTime <= request.StartTime) return BadRequest(new { message = "Giờ kết thúc phải sau giờ bắt đầu." });

        var overlaps = await _dbContext.Bookings.AnyAsync(booking =>
            booking.CourtId == request.CourtId && booking.Status != "Cancelled" &&
            booking.StartTime < request.EndTime && booking.EndTime > request.StartTime,
            cancellationToken);
        if (overlaps) return Conflict(new { message = "Khung giờ này đã có lịch đặt hoặc đã bị khóa." });

        var booking = new Booking
        {
            CourtId = request.CourtId,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            Status = "Blocked",
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Bookings.Add(booking);
        await _dbContext.SaveChangesAsync(cancellationToken);
        PublishScheduleChange(court, booking, "Created");

        return Ok(new OwnerScheduleItemResponse
        {
            BookingId = booking.BookingId,
            CourtId = court.CourtId,
            VenueId = court.VenueId,
            VenueName = court.Venue.VenueName,
            CourtNumber = court.CourtNumber,
            StartTime = booking.StartTime,
            EndTime = booking.EndTime,
            Status = booking.Status,
            IsOwnerBlock = true
        });
    }

    [HttpDelete("schedule/entries/{bookingId:int}")]
    public async Task<ActionResult> DeleteScheduleEntry(int bookingId, CancellationToken cancellationToken)
    {
        var booking = await _dbContext.Bookings
            .Include(item => item.Court)
            .SingleOrDefaultAsync(item => item.BookingId == bookingId && item.PlayerId == null && item.Status == "Blocked" && item.Court.Venue.Owner.UserId == CurrentUserId(), cancellationToken);
        if (booking is null) return NotFound(new { message = "Không tìm thấy lịch vận hành." });

        var notification = new ScheduleChangedEvent(booking.Court.VenueId, booking.CourtId, booking.StartTime, booking.EndTime, booking.OwnerEntryType ?? "Blocked", "Deleted");
        _dbContext.Bookings.Remove(booking);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _scheduleRealtime.Publish(notification);
        return NoContent();
    }

    [HttpDelete("schedule/blocks/{bookingId:int}")]
    public async Task<ActionResult> DeleteBlock(int bookingId, CancellationToken cancellationToken)
    {
        var booking = await _dbContext.Bookings
            .Include(item => item.Court)
            .SingleOrDefaultAsync(item => item.BookingId == bookingId && item.PlayerId == null && item.Status == "Blocked" && item.Court.Venue.Owner.UserId == CurrentUserId(), cancellationToken);
        if (booking is null) return NotFound(new { message = "Không tìm thấy khung giờ đã khóa." });

        var notification = new ScheduleChangedEvent(booking.Court.VenueId, booking.CourtId, booking.StartTime, booking.EndTime, booking.OwnerEntryType ?? "Blocked", "Deleted");
        _dbContext.Bookings.Remove(booking);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _scheduleRealtime.Publish(notification);
        return NoContent();
    }

    [HttpPatch("bookings/{bookingId:int}/status")]
    public async Task<ActionResult> UpdateBookingStatus(
        int bookingId,
        OwnerBookingStatusRequest request,
        CancellationToken cancellationToken)
    {
        var booking = await _dbContext.Bookings
            .Include(item => item.Court)
            .Include(item => item.Payments).ThenInclude(payment => payment.StatusHistories)
            .SingleOrDefaultAsync(item => item.BookingId == bookingId && item.PlayerId != null && item.Court.Venue.Owner.UserId == CurrentUserId(), cancellationToken);
        if (booking is null) return NotFound(new { message = "Không tìm thấy đơn đặt sân." });

        if (booking.Status is "Cancelled" or "Expired")
            return Conflict(new { message = "Không thể cập nhật booking đã hủy hoặc hết hạn." });
        if (request.Status == "Confirmed" && !booking.Payments.Any(payment => payment.Status == "Paid"))
            return Conflict(new { message = "Chỉ xác nhận booking sau khi thanh toán đã được duyệt." });

        var previousStatus = booking.Status;
        booking.Status = request.Status;
        booking.StatusHistories.Add(new BookingStatusHistory
        {
            FromStatus = previousStatus,
            ToStatus = request.Status,
            Reason = "Chủ sân cập nhật trạng thái",
            ActorUserId = CurrentUserId(),
            ChangedAt = DateTime.UtcNow
        });
        if (request.Status == "Cancelled")
        {
            foreach (var payment in booking.Payments.Where(item => item.Status is "Pending" or "WaitingForConfirmation"))
            {
                var previousPaymentStatus = payment.Status;
                payment.Status = "Cancelled";
                payment.StatusHistories.Add(new PaymentStatusHistory
                {
                    FromStatus = previousPaymentStatus,
                    ToStatus = "Cancelled",
                    Action = "BookingCancelled",
                    Reason = "Chủ sân hủy booking",
                    ActorUserId = CurrentUserId(),
                    CreatedAt = DateTime.UtcNow
                });
            }
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
        _scheduleRealtime.Publish(new ScheduleChangedEvent(
            booking.Court.VenueId,
            booking.CourtId,
            booking.StartTime,
            booking.EndTime,
            booking.Status,
            booking.Status == "Cancelled" ? "Deleted" : "Updated"));
        return Ok(new { booking.BookingId, booking.Status });
    }

    private async Task<VenueOwner?> GetOwnerAsync(bool createIfMissing, CancellationToken cancellationToken)
    {
        var userId = CurrentUserId();
        if (userId is null) return null;

        var owner = await _dbContext.VenueOwners.SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (owner is not null || !createIfMissing) return owner;

        owner = new VenueOwner { UserId = userId.Value };
        _dbContext.VenueOwners.Add(owner);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return owner;
    }

    private int? CurrentUserId() => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;

    private async Task<Venue?> GetOwnedVenue(int venueId, CancellationToken cancellationToken) =>
        await _dbContext.Venues
            .Include(venue => venue.Amenities)
            .Include(venue => venue.BookingRules)
            .Include(venue => venue.VenueImages)
            .Include(venue => venue.VenueListingPayments)
            .Include(venue => venue.VenueAuditLogs)
            .Include(venue => venue.Courts).ThenInclude(court => court.Bookings)
            .SingleOrDefaultAsync(venue => venue.VenueId == venueId && venue.Owner.UserId == CurrentUserId(), cancellationToken);

    private async Task<Court?> GetOwnedCourt(int courtId, CancellationToken cancellationToken) =>
        await _dbContext.Courts.Include(court => court.Venue)
            .SingleOrDefaultAsync(court => court.CourtId == courtId && court.Venue.Owner.UserId == CurrentUserId(), cancellationToken);

    private async Task<List<Venue>> LoadOwnerVenues(int ownerId, CancellationToken cancellationToken) =>
        await _dbContext.Venues.AsNoTracking()
            .Where(venue => venue.OwnerId == ownerId)
            .Include(venue => venue.Amenities)
            .Include(venue => venue.BookingRules)
            .Include(venue => venue.VenueImages)
            .Include(venue => venue.VenueListingPayments)
            .Include(venue => venue.Courts)
            .OrderBy(venue => venue.VenueName)
            .ToListAsync(cancellationToken);

    private void ApplyVenueDetails(Venue venue, OwnerVenueUpsertRequest request)
    {
        _dbContext.Amenities.RemoveRange(venue.Amenities);
        venue.Amenities = request.Amenities
            .Select(Normalize).Where(value => value is not null).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(value => new Amenity { VenueId = venue.VenueId, AmenityName = value!, IsFree = true }).ToList();

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
            BasePrice = double.TryParse(venue.BookingRules.FirstOrDefault(rule => rule.RuleType == "BasePrice")?.RuleContent, NumberStyles.Any, CultureInfo.InvariantCulture, out var price) ? price : 0,
            Amenities = venue.Amenities.Select(item => item.AmenityName).ToList(),
            Images = venue.VenueImages.OrderByDescending(image => image.IsPrimary).ThenBy(image => image.SortOrder).Select(MapImage).ToList(),
            Courts = venue.Courts.OrderBy(court => court.CourtNumber).Select(MapCourt).ToList()
        };
    }

    private async Task<decimal> GetCurrentListingPriceAsync(CancellationToken cancellationToken) =>
        await _dbContext.ListingFeeSettings.AsNoTracking()
            .OrderByDescending(setting => setting.UpdatedAt)
            .ThenByDescending(setting => setting.ListingFeeSettingId)
            .Select(setting => setting.PricePerCourtPerMonth)
            .FirstOrDefaultAsync(cancellationToken);

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

    private void MarkVenueChanged(Venue venue)
    {
        if (venue.ApprovalStatus is "Approved" or "Pending" or "Rejected")
        {
            venue.ApprovalStatus = "Draft";
            venue.RejectionReason = null;
        }
    }

    private void PublishScheduleChange(Court court, Booking booking, string action)
    {
        _scheduleRealtime.Publish(new ScheduleChangedEvent(
            court.VenueId,
            court.CourtId,
            booking.StartTime,
            booking.EndTime,
            booking.OwnerEntryType ?? "Blocked",
            action));
    }

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
        var webRoot = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        var directory = Path.Combine(webRoot, "uploads", "payment-receipts");
        Directory.CreateDirectory(directory);
        await using var stream = System.IO.File.Create(Path.Combine(directory, fileName));
        await receipt.CopyToAsync(stream, cancellationToken);
        return $"{Request.Scheme}://{Request.Host}/uploads/payment-receipts/{fileName}";
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
