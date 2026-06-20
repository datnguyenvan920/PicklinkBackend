using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize(Roles = "VenueOwner")]
[Route("api/owner")]
public class OwnerVenueController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;

    public OwnerVenueController(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
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
            OverallRating = 0
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
                SurfaceType = "Hard court",
                AvailabilityStatus = "Available"
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
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
        ApplyVenueDetails(venue, request);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(MapVenue(venue));
    }

    [HttpDelete("venues/{venueId:int}")]
    public async Task<ActionResult> DeleteVenue(int venueId, CancellationToken cancellationToken)
    {
        var venue = await GetOwnedVenue(venueId, cancellationToken);
        if (venue is null) return NotFound(new { message = "Không tìm thấy cụm sân." });

        if (venue.Courts.Any(court => court.Bookings.Count > 0))
            return Conflict(new { message = "Không thể xóa cụm sân đã có lịch đặt." });

        _dbContext.Amenities.RemoveRange(venue.Amenities);
        _dbContext.BookingRules.RemoveRange(venue.BookingRules);
        _dbContext.Courts.RemoveRange(venue.Courts);
        _dbContext.Venues.Remove(venue);
        await _dbContext.SaveChangesAsync(cancellationToken);
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
            SurfaceType = Normalize(request.SurfaceType),
            IsIndoor = request.IsIndoor,
            AvailabilityStatus = request.AvailabilityStatus
        };
        _dbContext.Courts.Add(court);
        await _dbContext.SaveChangesAsync(cancellationToken);
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
        court.SurfaceType = Normalize(request.SurfaceType);
        court.IsIndoor = request.IsIndoor;
        court.AvailabilityStatus = request.AvailabilityStatus;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(MapCourt(court));
    }

    [HttpDelete("courts/{courtId:int}")]
    public async Task<ActionResult> DeleteCourt(int courtId, CancellationToken cancellationToken)
    {
        var court = await GetOwnedCourt(courtId, cancellationToken);
        if (court is null) return NotFound(new { message = "Không tìm thấy sân con." });
        if (await _dbContext.Bookings.AnyAsync(booking => booking.CourtId == courtId, cancellationToken))
            return Conflict(new { message = "Không thể xóa sân con đã có lịch đặt." });

        _dbContext.Courts.Remove(court);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("schedule")]
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
            .Where(booking => booking.Court.Venue.OwnerId == owner.OwnerId && booking.StartTime < dayEnd && booking.EndTime > dayStart && booking.Status != "Cancelled")
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
            Status = "Blocked"
        };
        _dbContext.Bookings.Add(booking);
        await _dbContext.SaveChangesAsync(cancellationToken);

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

    [HttpDelete("schedule/blocks/{bookingId:int}")]
    public async Task<ActionResult> DeleteBlock(int bookingId, CancellationToken cancellationToken)
    {
        var booking = await _dbContext.Bookings
            .SingleOrDefaultAsync(item => item.BookingId == bookingId && item.PlayerId == null && item.Status == "Blocked" && item.Court.Venue.Owner.UserId == CurrentUserId(), cancellationToken);
        if (booking is null) return NotFound(new { message = "Không tìm thấy khung giờ đã khóa." });

        _dbContext.Bookings.Remove(booking);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPatch("bookings/{bookingId:int}/status")]
    public async Task<ActionResult> UpdateBookingStatus(
        int bookingId,
        OwnerBookingStatusRequest request,
        CancellationToken cancellationToken)
    {
        var booking = await _dbContext.Bookings
            .SingleOrDefaultAsync(item => item.BookingId == bookingId && item.PlayerId != null && item.Court.Venue.Owner.UserId == CurrentUserId(), cancellationToken);
        if (booking is null) return NotFound(new { message = "Không tìm thấy đơn đặt sân." });

        booking.Status = request.Status;
        await _dbContext.SaveChangesAsync(cancellationToken);
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

    private static OwnerVenueResponse MapVenue(Venue venue) => new()
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
        BasePrice = double.TryParse(venue.BookingRules.FirstOrDefault(rule => rule.RuleType == "BasePrice")?.RuleContent, NumberStyles.Any, CultureInfo.InvariantCulture, out var price) ? price : 0,
        Amenities = venue.Amenities.Select(item => item.AmenityName).ToList(),
        Courts = venue.Courts.OrderBy(court => court.CourtNumber).Select(MapCourt).ToList()
    };

    private static OwnerCourtResponse MapCourt(Court court) => new()
    {
        CourtId = court.CourtId,
        VenueId = court.VenueId,
        CourtNumber = court.CourtNumber,
        SurfaceType = court.SurfaceType,
        IsIndoor = court.IsIndoor,
        AvailabilityStatus = court.AvailabilityStatus
    };

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
