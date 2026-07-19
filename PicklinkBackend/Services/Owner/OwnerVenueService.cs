using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Services.Bookings;
using PicklinkBackend.Services.Schedules;
using PicklinkBackend.Services.Shared;
using PicklinkBackend.Services.Venues;

namespace PicklinkBackend.Services.Owner;
public sealed record OwnerVenueServiceDependencies(ApplicationDbContext DbContext, IWebHostEnvironment Environment, IConfiguration Configuration, ScheduleRealtimeNotifier ScheduleRealtime, VenueRealtimeNotifier VenueRealtime);

public class OwnerVenueService
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
    private readonly IConfiguration _configuration;
    private readonly ScheduleRealtimeNotifier _scheduleRealtime;
    private readonly VenueRealtimeNotifier _venueRealtime;

    public OwnerVenueService(
        ApplicationDbContext dbContext,
        IWebHostEnvironment environment,
        IConfiguration configuration,
        ScheduleRealtimeNotifier scheduleRealtime,
        VenueRealtimeNotifier venueRealtime)
    {
        _dbContext = dbContext;
        _environment = environment;
        _configuration = configuration;
        _scheduleRealtime = scheduleRealtime;
        _venueRealtime = venueRealtime;
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
        return venue is null ? NotFound(new { message = "KhÃƒÆ’Ã‚Â´ng tÃƒÆ’Ã‚Â¬m thÃƒÂ¡Ã‚ÂºÃ‚Â¥y cÃƒÂ¡Ã‚Â»Ã‚Â¥m sÃƒÆ’Ã‚Â¢n." }) : Ok(MapVenue(venue));
    }
    public async Task<ServiceResult<OwnerVenueResponse>> CreateVenue(
        OwnerVenueUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (request.CloseTime <= request.OpenTime)
            return BadRequest(new { message = "GiÃƒÂ¡Ã‚Â»Ã‚Â Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â³ng cÃƒÂ¡Ã‚Â»Ã‚Â­a phÃƒÂ¡Ã‚ÂºÃ‚Â£i sau giÃƒÂ¡Ã‚Â»Ã‚Â mÃƒÂ¡Ã‚Â»Ã…Â¸ cÃƒÂ¡Ã‚Â»Ã‚Â­a." });

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
    public async Task<ServiceResult<OwnerVenueResponse>> UpdateVenue(
        int venueId,
        OwnerVenueUpsertRequest request,
        CancellationToken cancellationToken)
    {
        if (request.CloseTime <= request.OpenTime)
            return BadRequest(new { message = "GiÃƒÂ¡Ã‚Â»Ã‚Â Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â³ng cÃƒÂ¡Ã‚Â»Ã‚Â­a phÃƒÂ¡Ã‚ÂºÃ‚Â£i sau giÃƒÂ¡Ã‚Â»Ã‚Â mÃƒÂ¡Ã‚Â»Ã…Â¸ cÃƒÂ¡Ã‚Â»Ã‚Â­a." });

        var venue = await GetOwnedVenue(venueId, cancellationToken);
        if (venue is null) return NotFound(new { message = "KhÃƒÆ’Ã‚Â´ng tÃƒÆ’Ã‚Â¬m thÃƒÂ¡Ã‚ÂºÃ‚Â¥y cÃƒÂ¡Ã‚Â»Ã‚Â¥m sÃƒÆ’Ã‚Â¢n." });

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
    public async Task<ServiceResult<OwnerVenueResponse>> SetVenueOpenStatus(
        int venueId,
        OwnerVenueOpenStatusRequest request,
        CancellationToken cancellationToken)
    {
        var venue = await GetOwnedVenue(venueId, cancellationToken);
        if (venue is null) return NotFound(new { message = "KhÃƒÆ’Ã‚Â´ng tÃƒÆ’Ã‚Â¬m thÃƒÂ¡Ã‚ÂºÃ‚Â¥y cÃƒÂ¡Ã‚Â»Ã‚Â¥m sÃƒÆ’Ã‚Â¢n." });

        venue.IsOpen = request.IsOpen;
        AddAuditLog(venue, request.IsOpen ? "OwnerOpenedVenue" : "OwnerClosedVenue");
        await _dbContext.SaveChangesAsync(cancellationToken);
        _venueRealtime.Publish(venueId, request.IsOpen ? "Opened" : "Closed");
        return Ok(MapVenue(venue));
    }
    public async Task<ServiceResult<OwnerVenueResponse>> SubmitVenueForApproval(
        int venueId,
        CancellationToken cancellationToken)
    {
        var venue = await GetOwnedVenue(venueId, cancellationToken);
        if (venue is null) return NotFound(new { message = "KhÃƒÆ’Ã‚Â´ng tÃƒÆ’Ã‚Â¬m thÃƒÂ¡Ã‚ÂºÃ‚Â¥y cÃƒÂ¡Ã‚Â»Ã‚Â¥m sÃƒÆ’Ã‚Â¢n." });
        if (venue.ApprovalStatus == "Pending")
            return Conflict(new { message = "CÃƒÂ¡Ã‚Â»Ã‚Â¥m sÃƒÆ’Ã‚Â¢n Ãƒâ€žÃ¢â‚¬Ëœang chÃƒÂ¡Ã‚Â»Ã‚Â Admin duyÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¡t." });
        if (venue.Courts.Count == 0)
            return BadRequest(new { message = "HÃƒÆ’Ã‚Â£y thÃƒÆ’Ã‚Âªm ÃƒÆ’Ã‚Â­t nhÃƒÂ¡Ã‚ÂºÃ‚Â¥t mÃƒÂ¡Ã‚Â»Ã¢â€žÂ¢t sÃƒÆ’Ã‚Â¢n con trÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã¢â‚¬Âºc khi gÃƒÂ¡Ã‚Â»Ã‚Â­i duyÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¡t." });
        if (venue.Latitude is null || venue.Longitude is null)
            return BadRequest(new { message = "HÃƒÆ’Ã‚Â£y Ãƒâ€žÃ¢â‚¬ËœÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¹nh vÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¹ cÃƒÂ¡Ã‚Â»Ã‚Â¥m sÃƒÆ’Ã‚Â¢n trÃƒÆ’Ã‚Âªn bÃƒÂ¡Ã‚ÂºÃ‚Â£n Ãƒâ€žÃ¢â‚¬ËœÃƒÂ¡Ã‚Â»Ã¢â‚¬Å“ trÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã¢â‚¬Âºc khi gÃƒÂ¡Ã‚Â»Ã‚Â­i duyÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¡t." });

        venue.ApprovalStatus = "Pending";
        venue.RejectionReason = null;
        AddAuditLog(venue, "OwnerSubmittedForApproval");
        await _dbContext.SaveChangesAsync(cancellationToken);
        _venueRealtime.Publish(venueId, "Submitted");
        return Ok(MapVenue(venue));
    }
    public async Task<ServiceResult<OwnerListingFeePreviewResponse>> PreviewListingFee(
        int venueId,
        int months = 1,
        CancellationToken cancellationToken = default)
    {
        if (months is < 1 or > 24)
            return BadRequest(new { message = "SÃƒÂ¡Ã‚Â»Ã¢â‚¬Ëœ thÃƒÆ’Ã‚Â¡ng phÃƒÂ¡Ã‚ÂºÃ‚Â£i tÃƒÂ¡Ã‚Â»Ã‚Â« 1 Ãƒâ€žÃ¢â‚¬ËœÃƒÂ¡Ã‚ÂºÃ‚Â¿n 24." });

        var venue = await GetOwnedVenue(venueId, cancellationToken);
        if (venue is null) return NotFound(new { message = "KhÃƒÆ’Ã‚Â´ng tÃƒÆ’Ã‚Â¬m thÃƒÂ¡Ã‚ÂºÃ‚Â¥y cÃƒÂ¡Ã‚Â»Ã‚Â¥m sÃƒÆ’Ã‚Â¢n." });

        var activeCourtCount = ActiveCourtCount(venue);
        if (activeCourtCount == 0)
            return BadRequest(new { message = "CÃƒÂ¡Ã‚Â»Ã‚Â¥m sÃƒÆ’Ã‚Â¢n cÃƒÂ¡Ã‚ÂºÃ‚Â§n ÃƒÆ’Ã‚Â­t nhÃƒÂ¡Ã‚ÂºÃ‚Â¥t mÃƒÂ¡Ã‚Â»Ã¢â€žÂ¢t sÃƒÆ’Ã‚Â¢n con Ãƒâ€žÃ¢â‚¬Ëœang hoÃƒÂ¡Ã‚ÂºÃ‚Â¡t Ãƒâ€žÃ¢â‚¬ËœÃƒÂ¡Ã‚Â»Ã¢â€žÂ¢ng Ãƒâ€žÃ¢â‚¬ËœÃƒÂ¡Ã‚Â»Ã†â€™ tÃƒÆ’Ã‚Â­nh phÃƒÆ’Ã‚Â­ lÃƒÆ’Ã‚Âªn sÃƒÆ’Ã‚Â n." });

        var price = await GetCurrentListingPriceAsync(cancellationToken);
        if (price <= 0)
            return Conflict(new { message = "Admin chÃƒâ€ Ã‚Â°a cÃƒÂ¡Ã‚ÂºÃ‚Â¥u hÃƒÆ’Ã‚Â¬nh Ãƒâ€žÃ¢â‚¬ËœÃƒâ€ Ã‚Â¡n giÃƒÆ’Ã‚Â¡ phÃƒÆ’Ã‚Â­ lÃƒÆ’Ã‚Âªn sÃƒÆ’Ã‚Â n." });

        return Ok(new OwnerListingFeePreviewResponse
        {
            VenueId = venueId,
            Months = months,
            ActiveCourtCount = activeCourtCount,
            PricePerCourtPerMonth = price,
            Amount = activeCourtCount * price * months
        });
    }
    public async Task<ServiceResult<OwnerListingFeePaymentResponse>> SubmitListingFeePayment(
        int venueId,
        OwnerListingFeePaymentRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Months is < 1 or > 24)
            return BadRequest(new { message = "SÃƒÂ¡Ã‚Â»Ã¢â‚¬Ëœ thÃƒÆ’Ã‚Â¡ng phÃƒÂ¡Ã‚ÂºÃ‚Â£i tÃƒÂ¡Ã‚Â»Ã‚Â« 1 Ãƒâ€žÃ¢â‚¬ËœÃƒÂ¡Ã‚ÂºÃ‚Â¿n 24." });
        if (request.Receipt is null || request.Receipt.Length == 0)
            return BadRequest(new { message = "Vui lÃƒÆ’Ã‚Â²ng tÃƒÂ¡Ã‚ÂºÃ‚Â£i ÃƒÂ¡Ã‚ÂºÃ‚Â£nh biÃƒÆ’Ã‚Âªn lai." });
        if (request.Receipt.Length > 5 * 1024 * 1024)
            return BadRequest(new { message = "ÃƒÂ¡Ã‚ÂºÃ‚Â¢nh biÃƒÆ’Ã‚Âªn lai khÃƒÆ’Ã‚Â´ng Ãƒâ€žÃ¢â‚¬ËœÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Â£c vÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Â£t quÃƒÆ’Ã‚Â¡ 5 MB." });
        if (!AllowedReceiptTypes.Contains(request.Receipt.ContentType))
            return BadRequest(new { message = "BiÃƒÆ’Ã‚Âªn lai chÃƒÂ¡Ã‚Â»Ã¢â‚¬Â° hÃƒÂ¡Ã‚Â»Ã¢â‚¬â€ trÃƒÂ¡Ã‚Â»Ã‚Â£ JPG, PNG hoÃƒÂ¡Ã‚ÂºÃ‚Â·c WEBP." });

        var venue = await GetOwnedVenue(venueId, cancellationToken);
        if (venue is null) return NotFound(new { message = "KhÃƒÆ’Ã‚Â´ng tÃƒÆ’Ã‚Â¬m thÃƒÂ¡Ã‚ÂºÃ‚Â¥y cÃƒÂ¡Ã‚Â»Ã‚Â¥m sÃƒÆ’Ã‚Â¢n." });

        var activeCourtCount = ActiveCourtCount(venue);
        if (activeCourtCount == 0)
            return BadRequest(new { message = "CÃƒÂ¡Ã‚Â»Ã‚Â¥m sÃƒÆ’Ã‚Â¢n cÃƒÂ¡Ã‚ÂºÃ‚Â§n ÃƒÆ’Ã‚Â­t nhÃƒÂ¡Ã‚ÂºÃ‚Â¥t mÃƒÂ¡Ã‚Â»Ã¢â€žÂ¢t sÃƒÆ’Ã‚Â¢n con Ãƒâ€žÃ¢â‚¬Ëœang hoÃƒÂ¡Ã‚ÂºÃ‚Â¡t Ãƒâ€žÃ¢â‚¬ËœÃƒÂ¡Ã‚Â»Ã¢â€žÂ¢ng Ãƒâ€žÃ¢â‚¬ËœÃƒÂ¡Ã‚Â»Ã†â€™ tÃƒÆ’Ã‚Â­nh phÃƒÆ’Ã‚Â­ lÃƒÆ’Ã‚Âªn sÃƒÆ’Ã‚Â n." });

        var price = await GetCurrentListingPriceAsync(cancellationToken);
        if (price <= 0)
            return Conflict(new { message = "Admin chÃƒâ€ Ã‚Â°a cÃƒÂ¡Ã‚ÂºÃ‚Â¥u hÃƒÆ’Ã‚Â¬nh Ãƒâ€žÃ¢â‚¬ËœÃƒâ€ Ã‚Â¡n giÃƒÆ’Ã‚Â¡ phÃƒÆ’Ã‚Â­ lÃƒÆ’Ã‚Âªn sÃƒÆ’Ã‚Â n." });

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
    public async Task<ServiceResult<OwnerVenueImageResponse>> UploadVenueImage(
        int venueId,
        OwnerVenueImageUploadRequest request,
        CancellationToken cancellationToken)
    {
        var venue = await GetOwnedVenue(venueId, cancellationToken);
        if (venue is null) return NotFound(new { message = "KhÃƒÆ’Ã‚Â´ng tÃƒÆ’Ã‚Â¬m thÃƒÂ¡Ã‚ÂºÃ‚Â¥y cÃƒÂ¡Ã‚Â»Ã‚Â¥m sÃƒÆ’Ã‚Â¢n." });
        if (venue.VenueImages.Count >= 10)
            return BadRequest(new { message = "MÃƒÂ¡Ã‚Â»Ã¢â‚¬â€i cÃƒÂ¡Ã‚Â»Ã‚Â¥m sÃƒÆ’Ã‚Â¢n Ãƒâ€žÃ¢â‚¬ËœÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Â£c tÃƒÂ¡Ã‚ÂºÃ‚Â£i tÃƒÂ¡Ã‚Â»Ã¢â‚¬Ëœi Ãƒâ€žÃ¢â‚¬Ëœa 10 ÃƒÂ¡Ã‚ÂºÃ‚Â£nh." });
        if (request.Image.Length == 0 || request.Image.Length > MaxVenueImageBytes)
            return BadRequest(new { message = "ÃƒÂ¡Ã‚ÂºÃ‚Â¢nh sÃƒÆ’Ã‚Â¢n phÃƒÂ¡Ã‚ÂºÃ‚Â£i cÃƒÆ’Ã‚Â³ dung lÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Â£ng tÃƒÂ¡Ã‚Â»Ã‚Â« 1 byte Ãƒâ€žÃ¢â‚¬ËœÃƒÂ¡Ã‚ÂºÃ‚Â¿n 5MB." });

        var extension = Path.GetExtension(request.Image.FileName);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedImageExtensions.Contains(extension))
            return BadRequest(new { message = "ChÃƒÂ¡Ã‚Â»Ã¢â‚¬Â° hÃƒÂ¡Ã‚Â»Ã¢â‚¬â€ trÃƒÂ¡Ã‚Â»Ã‚Â£ ÃƒÂ¡Ã‚ÂºÃ‚Â£nh JPG, PNG hoÃƒÂ¡Ã‚ÂºÃ‚Â·c WEBP." });

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
            ImageUrl = PublicUrl($"/uploads/venues/{venueId}/{fileName}"),
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
    public async Task<ServiceResult<OwnerVenueResponse>> SetPrimaryVenueImage(
        int venueId,
        int imageId,
        CancellationToken cancellationToken)
    {
        var venue = await GetOwnedVenue(venueId, cancellationToken);
        if (venue is null) return NotFound(new { message = "KhÃƒÆ’Ã‚Â´ng tÃƒÆ’Ã‚Â¬m thÃƒÂ¡Ã‚ÂºÃ‚Â¥y cÃƒÂ¡Ã‚Â»Ã‚Â¥m sÃƒÆ’Ã‚Â¢n." });
        if (venue.VenueImages.All(image => image.VenueImageId != imageId))
            return NotFound(new { message = "KhÃƒÆ’Ã‚Â´ng tÃƒÆ’Ã‚Â¬m thÃƒÂ¡Ã‚ÂºÃ‚Â¥y ÃƒÂ¡Ã‚ÂºÃ‚Â£nh sÃƒÆ’Ã‚Â¢n." });

        foreach (var image in venue.VenueImages) image.IsPrimary = image.VenueImageId == imageId;
        MarkVenueChanged(venue);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _venueRealtime.Publish(venueId, "PrimaryImageChanged");
        return Ok(MapVenue(venue));
    }
    public async Task<ServiceResult> DeleteVenueImage(
        int venueId,
        int imageId,
        CancellationToken cancellationToken)
    {
        var venue = await GetOwnedVenue(venueId, cancellationToken);
        if (venue is null) return NotFound(new { message = "KhÃƒÆ’Ã‚Â´ng tÃƒÆ’Ã‚Â¬m thÃƒÂ¡Ã‚ÂºÃ‚Â¥y cÃƒÂ¡Ã‚Â»Ã‚Â¥m sÃƒÆ’Ã‚Â¢n." });
        var image = venue.VenueImages.FirstOrDefault(item => item.VenueImageId == imageId);
        if (image is null) return NotFound(new { message = "KhÃƒÆ’Ã‚Â´ng tÃƒÆ’Ã‚Â¬m thÃƒÂ¡Ã‚ÂºÃ‚Â¥y ÃƒÂ¡Ã‚ÂºÃ‚Â£nh sÃƒÆ’Ã‚Â¢n." });

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
    public async Task<ServiceResult> DeleteVenue(int venueId, CancellationToken cancellationToken)
    {
        var venue = await GetOwnedVenue(venueId, cancellationToken);
        if (venue is null) return NotFound(new { message = "KhÃƒÆ’Ã‚Â´ng tÃƒÆ’Ã‚Â¬m thÃƒÂ¡Ã‚ÂºÃ‚Â¥y cÃƒÂ¡Ã‚Â»Ã‚Â¥m sÃƒÆ’Ã‚Â¢n." });

        if (venue.Courts.Any(court => court.Bookings.Count > 0))
            return Conflict(new { message = "KhÃƒÆ’Ã‚Â´ng thÃƒÂ¡Ã‚Â»Ã†â€™ xÃƒÆ’Ã‚Â³a cÃƒÂ¡Ã‚Â»Ã‚Â¥m sÃƒÆ’Ã‚Â¢n Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ cÃƒÆ’Ã‚Â³ lÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¹ch Ãƒâ€žÃ¢â‚¬ËœÃƒÂ¡Ã‚ÂºÃ‚Â·t." });

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
    public async Task<ServiceResult<OwnerCourtResponse>> CreateCourt(
        int venueId,
        OwnerCourtUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var venue = await GetOwnedVenue(venueId, cancellationToken);
        if (venue is null) return NotFound(new { message = "KhÃƒÆ’Ã‚Â´ng tÃƒÆ’Ã‚Â¬m thÃƒÂ¡Ã‚ÂºÃ‚Â¥y cÃƒÂ¡Ã‚Â»Ã‚Â¥m sÃƒÆ’Ã‚Â¢n." });
        if (venue.Courts.Any(court => court.CourtNumber == request.CourtNumber))
            return Conflict(new { message = "SÃƒÂ¡Ã‚Â»Ã¢â‚¬Ëœ sÃƒÆ’Ã‚Â¢n con Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ tÃƒÂ¡Ã‚Â»Ã¢â‚¬Å“n tÃƒÂ¡Ã‚ÂºÃ‚Â¡i trong cÃƒÂ¡Ã‚Â»Ã‚Â¥m sÃƒÆ’Ã‚Â¢n nÃƒÆ’Ã‚Â y." });

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
    public async Task<ServiceResult<OwnerCourtResponse>> UpdateCourt(
        int courtId,
        OwnerCourtUpsertRequest request,
        CancellationToken cancellationToken)
    {
        var court = await GetOwnedCourt(courtId, cancellationToken);
        if (court is null) return NotFound(new { message = "KhÃƒÆ’Ã‚Â´ng tÃƒÆ’Ã‚Â¬m thÃƒÂ¡Ã‚ÂºÃ‚Â¥y sÃƒÆ’Ã‚Â¢n con." });
        if (await _dbContext.Courts.AnyAsync(item => item.VenueId == court.VenueId && item.CourtId != courtId && item.CourtNumber == request.CourtNumber, cancellationToken))
            return Conflict(new { message = "SÃƒÂ¡Ã‚Â»Ã¢â‚¬Ëœ sÃƒÆ’Ã‚Â¢n con Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ tÃƒÂ¡Ã‚Â»Ã¢â‚¬Å“n tÃƒÂ¡Ã‚ÂºÃ‚Â¡i trong cÃƒÂ¡Ã‚Â»Ã‚Â¥m sÃƒÆ’Ã‚Â¢n nÃƒÆ’Ã‚Â y." });

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
    public async Task<ServiceResult> DeleteCourt(int courtId, CancellationToken cancellationToken)
    {
        var court = await GetOwnedCourt(courtId, cancellationToken);
        if (court is null) return NotFound(new { message = "KhÃƒÆ’Ã‚Â´ng tÃƒÆ’Ã‚Â¬m thÃƒÂ¡Ã‚ÂºÃ‚Â¥y sÃƒÆ’Ã‚Â¢n con." });
        if (await _dbContext.Bookings.AnyAsync(booking => booking.CourtId == courtId, cancellationToken))
            return Conflict(new { message = "KhÃƒÆ’Ã‚Â´ng thÃƒÂ¡Ã‚Â»Ã†â€™ xÃƒÆ’Ã‚Â³a sÃƒÆ’Ã‚Â¢n con Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ cÃƒÆ’Ã‚Â³ lÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¹ch Ãƒâ€žÃ¢â‚¬ËœÃƒÂ¡Ã‚ÂºÃ‚Â·t." });

        var venueId = court.VenueId;
        MarkVenueChanged(court.Venue);
        _dbContext.Courts.Remove(court);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _venueRealtime.Publish(venueId, "CourtDeleted");
        return NoContent();
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
            .AsSplitQuery()
            .Where(booking => booking.Court.Venue.OwnerId == owner.OwnerId &&
                (booking.Slots.Any(slot => slot.StartTime < rangeEnd && slot.EndTime > rangeStart) ||
                 (!booking.Slots.Any() && booking.StartTime < rangeEnd && booking.EndTime > rangeStart)) &&
                booking.Status != "Cancelled" && booking.Status != "Expired")
            .Include(booking => booking.Court).ThenInclude(court => court.Venue)
            .Include(booking => booking.Slots)
            .Include(booking => booking.CheckInGroups)
            .Include(booking => booking.Operation)
            .Include(booking => booking.Player).ThenInclude(player => player!.User)
            .OrderBy(booking => booking.StartTime)
            .ToListAsync(cancellationToken);

        var bookingIds = bookings.Select(booking => booking.BookingId).ToList();
        var payments = await _dbContext.Payments
            .AsNoTracking()
            .Where(payment => bookingIds.Contains(payment.BookingId))
            .OrderByDescending(payment => payment.PaymentId)
            .Select(payment => new { payment.BookingId, payment.Amount, payment.Status })
            .ToListAsync(cancellationToken);
        var latestPayments = payments
            .GroupBy(payment => payment.BookingId)
            .ToDictionary(group => group.Key, group => group.First());

        var localNow = DateTime.Now;
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
            CanCancel = !HasStartedSlot(booking, localNow),
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
    public async Task<ServiceResult<OwnerScheduleResponse>> GetSchedule(DateOnly date, CancellationToken cancellationToken)
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
    public async Task<ServiceResult<OwnerScheduleItemResponse>> CreateScheduleEntry(
        OwnerScheduleBlockRequest request,
        CancellationToken cancellationToken)
    {
        var court = await GetOwnedCourt(request.CourtId, cancellationToken);
        if (court is null) return NotFound(new { message = "KhÃƒÆ’Ã‚Â´ng tÃƒÆ’Ã‚Â¬m thÃƒÂ¡Ã‚ÂºÃ‚Â¥y sÃƒÆ’Ã‚Â¢n con." });
        if (request.EndTime <= request.StartTime)
            return BadRequest(new { message = "GiÃƒÂ¡Ã‚Â»Ã‚Â kÃƒÂ¡Ã‚ÂºÃ‚Â¿t thÃƒÆ’Ã‚Âºc phÃƒÂ¡Ã‚ÂºÃ‚Â£i sau giÃƒÂ¡Ã‚Â»Ã‚Â bÃƒÂ¡Ã‚ÂºÃ‚Â¯t Ãƒâ€žÃ¢â‚¬ËœÃƒÂ¡Ã‚ÂºÃ‚Â§u." });
        if (DateOnly.FromDateTime(request.StartTime) != DateOnly.FromDateTime(request.EndTime))
            return BadRequest(new { message = "Khung lÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¹ch phÃƒÂ¡Ã‚ÂºÃ‚Â£i bÃƒÂ¡Ã‚ÂºÃ‚Â¯t Ãƒâ€žÃ¢â‚¬ËœÃƒÂ¡Ã‚ÂºÃ‚Â§u vÃƒÆ’Ã‚Â  kÃƒÂ¡Ã‚ÂºÃ‚Â¿t thÃƒÆ’Ã‚Âºc trong cÃƒÆ’Ã‚Â¹ng mÃƒÂ¡Ã‚Â»Ã¢â€žÂ¢t ngÃƒÆ’Ã‚Â y." });
        if (request.StartTime.Minute % 30 != 0 || request.EndTime.Minute % 30 != 0 || request.StartTime.Second != 0 || request.EndTime.Second != 0 || (request.EndTime - request.StartTime).TotalMinutes % 30 != 0)
            return BadRequest(new { message = "ThÃƒÂ¡Ã‚Â»Ã‚Âi gian phÃƒÂ¡Ã‚ÂºÃ‚Â£i theo bÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã¢â‚¬Âºc 30 phÃƒÆ’Ã‚Âºt." });

        var slotDate = DateOnly.FromDateTime(request.StartTime);
        var opening = slotDate.ToDateTime(court.Venue.OpenTime);
        var closing = slotDate.ToDateTime(court.Venue.CloseTime);
        if (request.StartTime < opening || request.EndTime > closing)
            return BadRequest(new { message = $"Khung giÃƒÂ¡Ã‚Â»Ã‚Â phÃƒÂ¡Ã‚ÂºÃ‚Â£i nÃƒÂ¡Ã‚ÂºÃ‚Â±m trong giÃƒÂ¡Ã‚Â»Ã‚Â mÃƒÂ¡Ã‚Â»Ã…Â¸ cÃƒÂ¡Ã‚Â»Ã‚Â­a {court.Venue.OpenTime:HH:mm}ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Å“{court.Venue.CloseTime:HH:mm}." });
        if (request.EntryType == "Event" && string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new { message = "Vui lÃƒÆ’Ã‚Â²ng nhÃƒÂ¡Ã‚ÂºÃ‚Â­p tÃƒÆ’Ã‚Âªn sÃƒÂ¡Ã‚Â»Ã‚Â± kiÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¡n." });

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(
                _dbContext, transaction, $"court-booking:{request.CourtId}", cancellationToken))
            return Conflict(new { message = "Sân đang được cập nhật. Vui lòng thử lại." });
        var overlaps = await _dbContext.Bookings.AnyAsync(booking =>
            booking.Status != "Cancelled"
            && booking.Status != "Expired"
            && (booking.Status != "Holding" || booking.HoldExpiresAt > DateTime.UtcNow)
            && (booking.Slots.Any(slot => slot.CourtId == request.CourtId
                    && slot.StartTime < request.EndTime && slot.EndTime > request.StartTime)
                || !booking.Slots.Any() && booking.CourtId == request.CourtId
                    && booking.StartTime < request.EndTime && booking.EndTime > request.StartTime),
            cancellationToken);
        if (overlaps) return Conflict(new { message = "Khung giÃƒÂ¡Ã‚Â»Ã‚Â Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ cÃƒÆ’Ã‚Â³ booking hoÃƒÂ¡Ã‚ÂºÃ‚Â·c lÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¹ch vÃƒÂ¡Ã‚ÂºÃ‚Â­n hÃƒÆ’Ã‚Â nh khÃƒÆ’Ã‚Â¡c." });

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
        await transaction.CommitAsync(cancellationToken);
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
    public async Task<ServiceResult<OwnerScheduleItemResponse>> CreateBlock(
        OwnerScheduleBlockRequest request,
        CancellationToken cancellationToken)
    {
        var court = await GetOwnedCourt(request.CourtId, cancellationToken);
        if (court is null) return NotFound(new { message = "KhÃƒÆ’Ã‚Â´ng tÃƒÆ’Ã‚Â¬m thÃƒÂ¡Ã‚ÂºÃ‚Â¥y sÃƒÆ’Ã‚Â¢n con." });
        if (request.EndTime <= request.StartTime) return BadRequest(new { message = "GiÃƒÂ¡Ã‚Â»Ã‚Â kÃƒÂ¡Ã‚ÂºÃ‚Â¿t thÃƒÆ’Ã‚Âºc phÃƒÂ¡Ã‚ÂºÃ‚Â£i sau giÃƒÂ¡Ã‚Â»Ã‚Â bÃƒÂ¡Ã‚ÂºÃ‚Â¯t Ãƒâ€žÃ¢â‚¬ËœÃƒÂ¡Ã‚ÂºÃ‚Â§u." });

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        if (!await SqlServerBookingLock.AcquireAsync(
                _dbContext, transaction, $"court-booking:{request.CourtId}", cancellationToken))
            return Conflict(new { message = "Sân đang được cập nhật. Vui lòng thử lại." });
        var overlaps = await _dbContext.Bookings.AnyAsync(booking =>
            booking.Status != "Cancelled"
            && booking.Status != "Expired"
            && (booking.Status != "Holding" || booking.HoldExpiresAt > DateTime.UtcNow)
            && (booking.Slots.Any(slot => slot.CourtId == request.CourtId
                    && slot.StartTime < request.EndTime && slot.EndTime > request.StartTime)
                || !booking.Slots.Any() && booking.CourtId == request.CourtId
                    && booking.StartTime < request.EndTime && booking.EndTime > request.StartTime),
            cancellationToken);
        if (overlaps) return Conflict(new { message = "Khung giÃƒÂ¡Ã‚Â»Ã‚Â nÃƒÆ’Ã‚Â y Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ cÃƒÆ’Ã‚Â³ lÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¹ch Ãƒâ€žÃ¢â‚¬ËœÃƒÂ¡Ã‚ÂºÃ‚Â·t hoÃƒÂ¡Ã‚ÂºÃ‚Â·c Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ bÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¹ khÃƒÆ’Ã‚Â³a." });

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
        await transaction.CommitAsync(cancellationToken);
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
    public async Task<ServiceResult> DeleteScheduleEntry(int bookingId, CancellationToken cancellationToken)
    {
        var booking = await _dbContext.Bookings
            .Include(item => item.Court)
            .SingleOrDefaultAsync(item => item.BookingId == bookingId && item.PlayerId == null && item.Status == "Blocked" && item.Court.Venue.Owner.UserId == CurrentUserId(), cancellationToken);
        if (booking is null) return NotFound(new { message = "KhÃƒÆ’Ã‚Â´ng tÃƒÆ’Ã‚Â¬m thÃƒÂ¡Ã‚ÂºÃ‚Â¥y lÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¹ch vÃƒÂ¡Ã‚ÂºÃ‚Â­n hÃƒÆ’Ã‚Â nh." });

        var notification = new ScheduleChangedEvent(booking.Court.VenueId, booking.CourtId, booking.StartTime, booking.EndTime, booking.OwnerEntryType ?? "Blocked", "Deleted");
        _dbContext.Bookings.Remove(booking);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _scheduleRealtime.Publish(notification);
        return NoContent();
    }
    public async Task<ServiceResult> DeleteBlock(int bookingId, CancellationToken cancellationToken)
    {
        var booking = await _dbContext.Bookings
            .Include(item => item.Court)
            .SingleOrDefaultAsync(item => item.BookingId == bookingId && item.PlayerId == null && item.Status == "Blocked" && item.Court.Venue.Owner.UserId == CurrentUserId(), cancellationToken);
        if (booking is null) return NotFound(new { message = "KhÃƒÆ’Ã‚Â´ng tÃƒÆ’Ã‚Â¬m thÃƒÂ¡Ã‚ÂºÃ‚Â¥y khung giÃƒÂ¡Ã‚Â»Ã‚Â Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ khÃƒÆ’Ã‚Â³a." });

        var notification = new ScheduleChangedEvent(booking.Court.VenueId, booking.CourtId, booking.StartTime, booking.EndTime, booking.OwnerEntryType ?? "Blocked", "Deleted");
        _dbContext.Bookings.Remove(booking);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _scheduleRealtime.Publish(notification);
        return NoContent();
    }
    public async Task<ServiceResult> UpdateBookingStatus(
        int bookingId,
        OwnerBookingStatusRequest request,
        CancellationToken cancellationToken)
    {
        var booking = await _dbContext.Bookings
            .Include(item => item.Court)
            .Include(item => item.CheckInGroups)
            .Include(item => item.Slots)
            .Include(item => item.Operation)
            .Include(item => item.Payments).ThenInclude(payment => payment.StatusHistories)
            .SingleOrDefaultAsync(item => item.BookingId == bookingId && item.PlayerId != null && item.Court.Venue.Owner.UserId == CurrentUserId(), cancellationToken);
        if (booking is null) return NotFound(new { message = "KhÃƒÆ’Ã‚Â´ng tÃƒÆ’Ã‚Â¬m thÃƒÂ¡Ã‚ÂºÃ‚Â¥y Ãƒâ€žÃ¢â‚¬ËœÃƒâ€ Ã‚Â¡n Ãƒâ€žÃ¢â‚¬ËœÃƒÂ¡Ã‚ÂºÃ‚Â·t sÃƒÆ’Ã‚Â¢n." });

        if (booking.Status is "Cancelled" or "Expired")
            return Conflict(new { message = "KhÃƒÆ’Ã‚Â´ng thÃƒÂ¡Ã‚Â»Ã†â€™ cÃƒÂ¡Ã‚ÂºÃ‚Â­p nhÃƒÂ¡Ã‚ÂºÃ‚Â­t booking Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ hÃƒÂ¡Ã‚Â»Ã‚Â§y hoÃƒÂ¡Ã‚ÂºÃ‚Â·c hÃƒÂ¡Ã‚ÂºÃ‚Â¿t hÃƒÂ¡Ã‚ÂºÃ‚Â¡n." });
        if (request.Status == "Cancelled" && HasStartedSlot(booking, DateTime.Now))
            return Conflict(new { message = "Không thể hủy booking đã bắt đầu hoặc có slot thuộc quá khứ." });
        if (request.Status == "Confirmed" && !booking.Payments.Any(payment => payment.Status == "Paid"))
            return Conflict(new { message = "ChÃƒÂ¡Ã‚Â»Ã¢â‚¬Â° xÃƒÆ’Ã‚Â¡c nhÃƒÂ¡Ã‚ÂºÃ‚Â­n booking sau khi thanh toÃƒÆ’Ã‚Â¡n Ãƒâ€žÃ¢â‚¬ËœÃƒÆ’Ã‚Â£ Ãƒâ€žÃ¢â‚¬ËœÃƒâ€ Ã‚Â°ÃƒÂ¡Ã‚Â»Ã‚Â£c duyÃƒÂ¡Ã‚Â»Ã¢â‚¬Â¡t." });

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

    private static bool HasStartedSlot(Booking booking, DateTime localNow)
    {
        if (booking.Slots.Count > 0)
            return booking.Slots.Any(slot => localNow >= slot.StartTime);

        return localNow >= booking.StartTime;
    }

    private static string GetBookingCheckInStatus(Booking booking, DateTime localNow)
    {
        if (booking.CheckInGroups.Count == 0)
            return GetStoredCheckInStatus(booking.Operation?.CheckInStatus, booking.Status, booking.StartTime, localNow);

        if (booking.CheckInGroups.All(group => group.CheckInStatus == "CheckedIn")) return "CheckedIn";
        if (booking.CheckInGroups.Any(group => group.CheckInStatus == "CheckedIn")) return "PartiallyCheckedIn";
        if (booking.CheckInGroups.All(group => group.CheckInStatus == "NoShow")) return "NoShow";
        return booking.CheckInGroups.Any(group => localNow >= group.StartTime.AddMinutes(-30)) ? "Ready" : "NotOpen";
    }

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

    public void SetCurrentUserId(int? userId) => _currentUserId = userId;

    private int? _currentUserId;

    private int? CurrentUserId() => _currentUserId;

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
            .AsSplitQuery()
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
            BasePrice = decimal.TryParse(venue.BookingRules.FirstOrDefault(rule => rule.RuleType == "BasePrice")?.RuleContent, NumberStyles.Any, CultureInfo.InvariantCulture, out var price) ? price : 0,
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
        return PublicUrl($"/uploads/payment-receipts/{fileName}");
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
