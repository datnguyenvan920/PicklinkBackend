using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NUnit.Framework;
using PicklinkBackend.Data;
using PicklinkBackend.Models;
using PicklinkBackend.Repositories.Implementations;

namespace Picklink.Test.Repositories;

[TestFixture]
public class BookingRepositoryMatchIsolationTests
{
    private const int PlayerUserId = 4101;
    private const int PlayerId = 4201;
    private const int DirectBookingId = 4301;
    private const int MatchBookingId = 4302;

    private ApplicationDbContext _dbContext = null!;
    private BookingRepository _repository = null!;
    private Guid _paymentGroupId;

    [SetUp]
    public async Task SetUp()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _repository = new BookingRepository(_dbContext);
        _paymentGroupId = Guid.NewGuid();

        var playerUser = new User
        {
            UserId = PlayerUserId,
            Username = "booking-player",
            Email = "booking-player@test.local",
            PasswordHash = "test",
            UserType = "Player"
        };
        var player = new Player
        {
            PlayerId = PlayerId,
            UserId = PlayerUserId
        };
        var ownerUser = new User
        {
            UserId = 4102,
            Username = "booking-owner",
            Email = "booking-owner@test.local",
            PasswordHash = "test",
            UserType = "VenueOwner"
        };
        var owner = new VenueOwner
        {
            OwnerId = 4202,
            UserId = ownerUser.UserId
        };
        var venue = new Venue
        {
            VenueId = 4401,
            OwnerId = owner.OwnerId,
            VenueName = "Isolation venue",
            Address = "Test address",
            OpenTime = new TimeOnly(6, 0),
            CloseTime = new TimeOnly(22, 0),
            ApprovalStatus = "Approved"
        };
        var court = new Court
        {
            CourtId = 4501,
            VenueId = venue.VenueId,
            CourtNumber = 1,
            HourlyPrice = 100000,
            AvailabilityStatus = "Available"
        };

        var directBooking = CreateBooking(DirectBookingId, court.CourtId, matchId: null);
        var matchBooking = CreateBooking(MatchBookingId, court.CourtId, matchId: 4601);
        directBooking.Payments.Add(CreatePayment(4701, DirectBookingId));
        matchBooking.Payments.Add(CreatePayment(4702, MatchBookingId));

        await _dbContext.AddRangeAsync(
            playerUser,
            player,
            ownerUser,
            owner,
            venue,
            court,
            directBooking,
            matchBooking);
        await _dbContext.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown()
    {
        _dbContext.Dispose();
    }

    [Test]
    public async Task GetMyBookingsQueryable_ReturnsOnlyDirectCourtBookings()
    {
        var bookingIds = await _repository.GetMyBookingsQueryable(PlayerUserId)
            .Select(booking => booking.BookingId)
            .ToListAsync();

        Assert.That(bookingIds, Is.EqualTo(new[] { DirectBookingId }));
    }

    [Test]
    public async Task OwnedBookingQueries_DoNotExposeMatchBookings()
    {
        var mutableMatchBooking = await _repository.GetOwnedBookingAsync(
            MatchBookingId,
            PlayerUserId);
        var readOnlyMatchBooking = await _repository.GetOwnedBookingReadAsync(
            MatchBookingId,
            PlayerUserId);
        var directBooking = await _repository.GetOwnedBookingReadAsync(
            DirectBookingId,
            PlayerUserId);

        Assert.That(mutableMatchBooking, Is.Null);
        Assert.That(readOnlyMatchBooking, Is.Null);
        Assert.That(directBooking, Is.Not.Null);
    }

    [Test]
    public async Task GetHoldingGroupBookingsAsync_ReturnsOnlyDirectCourtBookings()
    {
        var bookings = await _repository.GetHoldingGroupBookingsAsync(
            _paymentGroupId,
            PlayerUserId);

        Assert.That(
            bookings.Select(booking => booking.BookingId),
            Is.EqualTo(new[] { DirectBookingId }));
    }

    private static Booking CreateBooking(int bookingId, int courtId, int? matchId)
    {
        return new Booking
        {
            BookingId = bookingId,
            PlayerId = PlayerId,
            CourtId = courtId,
            MatchId = matchId,
            BookingCode = $"TEST-{bookingId}",
            StartTime = DateTime.UtcNow.AddDays(1),
            EndTime = DateTime.UtcNow.AddDays(1).AddHours(1),
            Status = "Confirmed",
            CreatedAt = DateTime.UtcNow,
            HourlyPriceSnapshot = 100000,
            CourtAmount = 100000,
            TotalAmount = 100000
        };
    }

    private Payment CreatePayment(int paymentId, int bookingId)
    {
        return new Payment
        {
            PaymentId = paymentId,
            BookingId = bookingId,
            PayerId = PlayerId,
            PaymentGroupId = _paymentGroupId,
            Amount = 100000,
            PaymentMethod = "BankTransfer",
            Status = "Paid",
            PaidAt = DateTime.UtcNow
        };
    }
}
