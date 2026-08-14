using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Repositories.Implementations;
using PicklinkBackend.Services.Bookings.Implementations;
using PicklinkBackend.Services.Schedules;
using PicklinkBackend.Services.Shared;

namespace Picklink.Test.Services.Bookings
{
    [TestFixture]
    public class PlayerBookingServiceTests
    {
        private ApplicationDbContext _dbContext;
        private BookingRepository _bookingRepository;
        private VenueRepository _venueRepository;
        private UserRepository _userRepository;
        private IConfiguration _configuration;
        private ScheduleRealtimeNotifier _scheduleRealtime;
        private PlayerScheduleConflictService _playerScheduleConflict;
        private PlayerBookingService _playerBookingService;

        [SetUp]
        public void Setup()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            _dbContext = new ApplicationDbContext(options);
            _bookingRepository = new BookingRepository(_dbContext);
            _venueRepository = new VenueRepository(_dbContext);
            _userRepository = new UserRepository(_dbContext);

            var inMemorySettings = new Dictionary<string, string?>
            {
                { "Booking:HoldingMinutes", "5" }
            };
            _configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();

            _scheduleRealtime = new ScheduleRealtimeNotifier();
            _playerScheduleConflict = new PlayerScheduleConflictService(_bookingRepository);

            var deps = new PlayerBookingServiceDependencies(
                _bookingRepository,
                _venueRepository,
                _userRepository,
                _configuration,
                _scheduleRealtime,
                _playerScheduleConflict);

            _playerBookingService = new PlayerBookingService(deps);
        }

        [TearDown]
        public void TearDown()
        {
            _dbContext.Dispose();
        }

        private static User CreateUser(int userId, string username, string email, string userType = "Player")
        {
            return new User
            {
                UserId = userId,
                Username = username,
                Email = email,
                PasswordHash = "hash123",
                UserType = userType,
                IsLocked = false
            };
        }

        private static Venue CreateVenue(int venueId, int ownerId, string name, string address, string approvalStatus = "Approved", decimal basePrice = 100000)
        {
            var venue = new Venue
            {
                VenueId = venueId,
                OwnerId = ownerId,
                VenueName = name,
                Address = address,
                OpenTime = new TimeOnly(6, 0),
                CloseTime = new TimeOnly(22, 0),
                ApprovalStatus = approvalStatus,
                IsOpen = true,
                OverallRating = 4.5
            };

            venue.BookingRules.Add(new BookingRule
            {
                RuleId = venueId * 10,
                VenueId = venueId,
                RuleType = "BasePrice",
                RuleContent = basePrice.ToString()
            });

            return venue;
        }

        private static Court CreateCourt(int courtId, int venueId, int courtNumber, decimal hourlyPrice = 120000)
        {
            return new Court
            {
                CourtId = courtId,
                VenueId = venueId,
                CourtNumber = courtNumber,
                HourlyPrice = hourlyPrice,
                AvailabilityStatus = "Available",
                IsIndoor = false
            };
        }

        #region 1. GetVenues Tests (6 test cases)

        [Test]
        public async Task GetVenues_1_WithValidFilters_ReturnsPaginatedVenues()
        {
            // Arrange
            var owner = CreateUser(1, "owner1", "owner1@test.vn", "VenueOwner");
            var venueOwner = new VenueOwner { OwnerId = 1, UserId = 1 };
            var venue = CreateVenue(1, 1, "Pickleball Star", "Cầu Giấy, Hà Nội");
            var court = CreateCourt(1, 1, 1, 150000);
            venue.Courts.Add(court);

            await _dbContext.Users.AddAsync(owner);
            await _dbContext.VenueOwners.AddAsync(venueOwner);
            await _dbContext.Venues.AddAsync(venue);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _playerBookingService.GetVenues(null, null, null, null, false, 1, 10, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.Success));
            Assert.That(result.Value, Is.Not.Null);
            Assert.That(result.Value!.Items.Count, Is.EqualTo(1));
            Assert.That(result.Value.Items[0].VenueName, Is.EqualTo("Pickleball Star"));
        }

        [Test]
        public async Task GetVenues_2_WhenMinPriceGreaterThanMaxPrice_ReturnsBadRequest()
        {
            // Act
            var result = await _playerBookingService.GetVenues(null, null, 200000, 100000, false, 1, 10, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.BadRequest));
        }

        [Test]
        public async Task GetVenues_3_WhenFavoritesOnlyAndNoFavorites_ReturnsEmptyPagination()
        {
            // Arrange
            _playerBookingService.SetCurrentUserId(10); // User with no favorites

            // Act
            var result = await _playerBookingService.GetVenues(null, null, null, null, favoritesOnly: true, 1, 10, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.Success));
            Assert.That(result.Value!.TotalCount, Is.EqualTo(0));
            Assert.That(result.Value.Items, Is.Empty);
        }

        [Test]
        public async Task GetVenues_4_FiltersByKeywordInNameOrAddress_ReturnsMatchingVenues()
        {
            // Arrange
            var owner = CreateUser(2, "owner2", "owner2@test.vn", "VenueOwner");
            var venueOwner = new VenueOwner { OwnerId = 2, UserId = 2 };
            var venue1 = CreateVenue(2, 2, "Hanoi Arena", "Ba Đình, Hà Nội");
            var venue2 = CreateVenue(3, 2, "Saigon Court", "Quận 1, TP.HCM");

            await _dbContext.Users.AddAsync(owner);
            await _dbContext.VenueOwners.AddAsync(venueOwner);
            await _dbContext.Venues.AddRangeAsync(venue1, venue2);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _playerBookingService.GetVenues("Saigon", null, null, null, false, 1, 10, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.Success));
            Assert.That(result.Value!.Items.Count, Is.EqualTo(1));
            Assert.That(result.Value.Items[0].VenueName, Is.EqualTo("Saigon Court"));
        }

        [Test]
        public async Task GetVenues_5_FiltersByArea_ReturnsVenuesInArea()
        {
            // Arrange
            var owner = CreateUser(3, "owner3", "owner3@test.vn", "VenueOwner");
            var venueOwner = new VenueOwner { OwnerId = 3, UserId = 3 };
            var venue1 = CreateVenue(4, 3, "Court A", "Tây Hồ, Hà Nội");
            var venue2 = CreateVenue(5, 3, "Court B", "Đống Đa, Hà Nội");

            await _dbContext.Users.AddAsync(owner);
            await _dbContext.VenueOwners.AddAsync(venueOwner);
            await _dbContext.Venues.AddRangeAsync(venue1, venue2);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _playerBookingService.GetVenues(null, "Tây Hồ", null, null, false, 1, 10, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.Success));
            Assert.That(result.Value!.Items.Count, Is.EqualTo(1));
            Assert.That(result.Value.Items[0].Address, Does.Contain("Tây Hồ"));
        }

        [Test]
        public async Task GetVenues_6_WhenMinPriceOrMaxPriceNegative_ReturnsBadRequest()
        {
            // Act
            var result = await _playerBookingService.GetVenues(null, null, -50000, null, false, 1, 10, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.BadRequest));
        }

        #endregion

        #region 2. FavoriteVenues Tests (6 test cases)

        [Test]
        public async Task AddFavoriteVenue_1_WhenUserNotAuthenticated_ReturnsUnauthorized()
        {
            // Arrange
            _playerBookingService.SetCurrentUserId(null);

            // Act
            var result = await _playerBookingService.AddFavoriteVenue(1, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.Unauthorized));
        }

        [Test]
        public async Task AddFavoriteVenue_2_WhenVenueNotFound_ReturnsNotFound()
        {
            // Arrange
            var user = CreateUser(1, "user1", "user1@test.vn");
            var player = new Player { PlayerId = 1, UserId = 1 };
            await _dbContext.Users.AddAsync(user);
            await _dbContext.Players.AddAsync(player);
            await _dbContext.SaveChangesAsync();

            _playerBookingService.SetCurrentUserId(1);

            // Act
            var result = await _playerBookingService.AddFavoriteVenue(999, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.NotFound));
        }

        [Test]
        public async Task AddFavoriteVenue_3_WhenValid_AddsFavoriteVenueSuccessfully()
        {
            // Arrange
            var user = CreateUser(10, "fan", "fan@test.vn");
            var player = new Player { PlayerId = 10, UserId = 10 };
            var owner = CreateUser(11, "owner11", "owner11@test.vn", "VenueOwner");
            var venueOwner = new VenueOwner { OwnerId = 11, UserId = 11 };
            var venue = CreateVenue(10, 11, "Fav Venue", "Hanoi");

            await _dbContext.Users.AddRangeAsync(user, owner);
            await _dbContext.Players.AddAsync(player);
            await _dbContext.VenueOwners.AddAsync(venueOwner);
            await _dbContext.Venues.AddAsync(venue);
            await _dbContext.SaveChangesAsync();

            _playerBookingService.SetCurrentUserId(10);

            // Act
            var result = await _playerBookingService.AddFavoriteVenue(10, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.NoContent));
            var exists = await _dbContext.FavoriteVenues.AnyAsync(f => f.PlayerId == 10 && f.VenueId == 10);
            Assert.That(exists, Is.True);
        }

        [Test]
        public async Task RemoveFavoriteVenue_4_WhenUserNotAuthenticated_ReturnsUnauthorized()
        {
            // Arrange
            _playerBookingService.SetCurrentUserId(null);

            // Act
            var result = await _playerBookingService.RemoveFavoriteVenue(1, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.Unauthorized));
        }

        [Test]
        public async Task RemoveFavoriteVenue_5_WhenFavoriteExists_RemovesSuccessfully()
        {
            // Arrange
            var user = CreateUser(12, "fav_user", "fav@test.vn");
            var player = new Player { PlayerId = 12, UserId = 12 };
            var owner = CreateUser(13, "owner13", "owner13@test.vn", "VenueOwner");
            var venueOwner = new VenueOwner { OwnerId = 13, UserId = 13 };
            var venue = CreateVenue(12, 13, "Venue to remove", "Hanoi");
            var fav = new FavoriteVenue { PlayerId = 12, VenueId = 12 };

            await _dbContext.Users.AddRangeAsync(user, owner);
            await _dbContext.Players.AddAsync(player);
            await _dbContext.VenueOwners.AddAsync(venueOwner);
            await _dbContext.Venues.AddAsync(venue);
            await _dbContext.FavoriteVenues.AddAsync(fav);
            await _dbContext.SaveChangesAsync();

            _playerBookingService.SetCurrentUserId(12);

            // Act
            var result = await _playerBookingService.RemoveFavoriteVenue(12, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.NoContent));
            var exists = await _dbContext.FavoriteVenues.AnyAsync(f => f.PlayerId == 12 && f.VenueId == 12);
            Assert.That(exists, Is.False);
        }

        [Test]
        public async Task GetFavoriteVenues_6_WhenUserHasFavorites_ReturnsPaginatedFavoriteVenues()
        {
            // Arrange
            var user = CreateUser(14, "has_fav", "hasfav@test.vn");
            var player = new Player { PlayerId = 14, UserId = 14 };
            var owner = CreateUser(15, "owner15", "owner15@test.vn", "VenueOwner");
            var venueOwner = new VenueOwner { OwnerId = 15, UserId = 15 };
            var venue = CreateVenue(14, 15, "My Favorite Stadium", "Hanoi");
            var fav = new FavoriteVenue { PlayerId = 14, VenueId = 14 };

            await _dbContext.Users.AddRangeAsync(user, owner);
            await _dbContext.Players.AddAsync(player);
            await _dbContext.VenueOwners.AddAsync(venueOwner);
            await _dbContext.Venues.AddAsync(venue);
            await _dbContext.FavoriteVenues.AddAsync(fav);
            await _dbContext.SaveChangesAsync();

            _playerBookingService.SetCurrentUserId(14);

            // Act
            var result = await _playerBookingService.GetFavoriteVenues(1, 10, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.Success));
            Assert.That(result.Value!.Items.Count, Is.EqualTo(1));
            Assert.That(result.Value.Items[0].VenueName, Is.EqualTo("My Favorite Stadium"));
        }

        #endregion

        #region 3. GetAvailability Tests (6 test cases)

        [TestCase(999)]
        [TestCase(0)]
        [TestCase(-1)]
        public async Task GetAvailability_1_WhenVenueNotFoundOrInvalid_ReturnsNotFound(int invalidVenueId)
        {
            // Act
            var result = await _playerBookingService.GetAvailability(invalidVenueId, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.NotFound));
        }

        [Test]
        public async Task GetAvailability_2_WhenVenueIsClosed_MarksSlotsAsClosed()
        {
            // Arrange
            var owner = CreateUser(20, "owner20", "owner20@test.vn", "VenueOwner");
            var venueOwner = new VenueOwner { OwnerId = 20, UserId = 20 };
            var venue = CreateVenue(20, 20, "Closed Venue", "Hanoi");
            venue.IsOpen = false; // Closed
            var court = CreateCourt(20, 20, 1, 100000);
            venue.Courts.Add(court);

            await _dbContext.Users.AddAsync(owner);
            await _dbContext.VenueOwners.AddAsync(venueOwner);
            await _dbContext.Venues.AddAsync(venue);
            await _dbContext.SaveChangesAsync();

            var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

            // Act
            var result = await _playerBookingService.GetAvailability(20, tomorrow, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.Success));
            Assert.That(result.Value!.Slots.All(s => s.Status == "Closed"), Is.True);
        }

        [Test]
        public async Task GetAvailability_3_WithTodayDate_ReturnsCourtsAndAvailabilitySlots()
        {
            // Arrange
            var owner = CreateUser(21, "owner21", "owner21@test.vn", "VenueOwner");
            var venueOwner = new VenueOwner { OwnerId = 21, UserId = 21 };
            var venue = CreateVenue(21, 21, "Active Venue", "Hanoi");
            var court = CreateCourt(21, 21, 1, 100000);
            venue.Courts.Add(court);

            await _dbContext.Users.AddAsync(owner);
            await _dbContext.VenueOwners.AddAsync(venueOwner);
            await _dbContext.Venues.AddAsync(venue);
            await _dbContext.SaveChangesAsync();

            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            // Act
            var result = await _playerBookingService.GetAvailability(21, today, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.Success));
            Assert.That(result.Value, Is.Not.Null);
            Assert.That(result.Value!.Date, Is.EqualTo(today));
            Assert.That(result.Value.Courts.Count, Is.EqualTo(1));
            Assert.That(result.Value.Courts[0].CourtNumber, Is.EqualTo(1));
            Assert.That(result.Value.Slots.Count, Is.GreaterThan(0));
        }

        [Test]
        public async Task GetAvailability_4_MarksLockedOrBookedSlotsAsUnavailable()
        {
            // Arrange
            var owner = CreateUser(22, "owner22", "owner22@test.vn", "VenueOwner");
            var venueOwner = new VenueOwner { OwnerId = 22, UserId = 22 };
            var venue = CreateVenue(22, 22, "Booked Venue", "Hanoi");
            var court = CreateCourt(22, 22, 1, 100000);
            venue.Courts.Add(court);

            var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
            var slotStart = tomorrow.ToDateTime(new TimeOnly(8, 0));
            var slotEnd = tomorrow.ToDateTime(new TimeOnly(9, 0));

            var booking = new Booking
            {
                BookingId = 22,
                CourtId = 22,
                StartTime = slotStart,
                EndTime = slotEnd,
                Status = "Confirmed",
                CreatedAt = DateTime.UtcNow
            };

            await _dbContext.Users.AddAsync(owner);
            await _dbContext.VenueOwners.AddAsync(venueOwner);
            await _dbContext.Venues.AddAsync(venue);
            await _dbContext.Bookings.AddAsync(booking);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _playerBookingService.GetAvailability(22, tomorrow, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.Success));
            var bookedSlot = result.Value!.Slots.FirstOrDefault(s => s.StartTime.Hour == 8 && s.CourtId == 22);
            Assert.That(bookedSlot, Is.Not.Null);
            Assert.That(bookedSlot!.Status, Is.Not.EqualTo("Available"));
        }

        [Test]
        public async Task GetAvailability_5_CalculatesHourlyPricePerSlotCorrectly()
        {
            // Arrange
            var owner = CreateUser(23, "owner23", "owner23@test.vn", "VenueOwner");
            var venueOwner = new VenueOwner { OwnerId = 23, UserId = 23 };
            var venue = CreateVenue(23, 23, "Priced Venue", "Hanoi", basePrice: 80000);
            var court = CreateCourt(23, 23, 1, 150000);
            venue.Courts.Add(court);

            await _dbContext.Users.AddAsync(owner);
            await _dbContext.VenueOwners.AddAsync(venueOwner);
            await _dbContext.Venues.AddAsync(venue);
            await _dbContext.SaveChangesAsync();

            var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

            // Act
            var result = await _playerBookingService.GetAvailability(23, tomorrow, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.Success));
            Assert.That(result.Value!.Courts[0].HourlyPrice, Is.EqualTo(150000));
        }

        [Test]
        public async Task GetAvailability_6_WhenVenueHasNoCourts_ReturnsEmptyCourtsList()
        {
            // Arrange
            var owner = CreateUser(24, "owner24", "owner24@test.vn", "VenueOwner");
            var venueOwner = new VenueOwner { OwnerId = 24, UserId = 24 };
            var venue = CreateVenue(24, 24, "No Courts Venue", "Hanoi");
            await _dbContext.Users.AddAsync(owner);
            await _dbContext.VenueOwners.AddAsync(venueOwner);
            await _dbContext.Venues.AddAsync(venue);
            await _dbContext.SaveChangesAsync();

            var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

            // Act
            var result = await _playerBookingService.GetAvailability(24, tomorrow, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.Success));
            Assert.That(result.Value!.Courts, Is.Empty);
        }

        #endregion

        #region 4. CreateHolding Tests (6 test cases)

        [Test]
        public async Task CreateHolding_1_WhenUserNotAuthenticated_ReturnsUnauthorized()
        {
            // Arrange
            _playerBookingService.SetCurrentUserId(null);

            // Act
            var result = await _playerBookingService.CreateHolding(new CreateBookingHoldRequest(), CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.Unauthorized));
        }

        [Test]
        public async Task CreateHolding_2_WhenPlayerNotFound_ReturnsBadRequest()
        {
            // Arrange
            _playerBookingService.SetCurrentUserId(999);

            // Act
            var result = await _playerBookingService.CreateHolding(new CreateBookingHoldRequest(), CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.BadRequest));
        }

        [Test]
        public async Task CreateHolding_3_WhenSlotsEmpty_ReturnsBadRequest()
        {
            // Arrange
            var user = CreateUser(30, "p30", "p30@test.vn");
            var player = new Player { PlayerId = 30, UserId = 30 };
            await _dbContext.Users.AddAsync(user);
            await _dbContext.Players.AddAsync(player);
            await _dbContext.SaveChangesAsync();

            _playerBookingService.SetCurrentUserId(30);

            var req = new CreateBookingHoldRequest
            {
                Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
                Slots = new List<CreateBookingHoldSlotRequest>()
            };

            // Act
            var result = await _playerBookingService.CreateHolding(req, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.BadRequest));
        }

        [Test]
        public async Task CreateHolding_4_WhenSlotIsAlreadyBooked_ReturnsConflict()
        {
            // Arrange
            var user = CreateUser(31, "p31", "p31@test.vn");
            var player = new Player { PlayerId = 31, UserId = 31 };
            var owner = CreateUser(32, "owner32", "owner32@test.vn", "VenueOwner");
            var venueOwner = new VenueOwner { OwnerId = 32, UserId = 32 };
            var venue = CreateVenue(30, 32, "Hold Conflict Venue", "Hanoi");
            var court = CreateCourt(30, 30, 1, 100000);
            venue.Courts.Add(court);

            var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
            var slotStart = tomorrow.ToDateTime(new TimeOnly(14, 0));
            var slotEnd = tomorrow.ToDateTime(new TimeOnly(15, 0));

            var existingBooking = new Booking
            {
                BookingId = 30,
                CourtId = 30,
                StartTime = slotStart,
                EndTime = slotEnd,
                Status = "Holding",
                HoldExpiresAt = DateTime.UtcNow.AddMinutes(5),
                CreatedAt = DateTime.UtcNow
            };

            await _dbContext.Users.AddRangeAsync(user, owner);
            await _dbContext.Players.AddAsync(player);
            await _dbContext.VenueOwners.AddAsync(venueOwner);
            await _dbContext.Venues.AddAsync(venue);
            await _dbContext.Bookings.AddAsync(existingBooking);
            await _dbContext.SaveChangesAsync();

            _playerBookingService.SetCurrentUserId(31);

            var req = new CreateBookingHoldRequest
            {
                Date = tomorrow,
                Slots = new List<CreateBookingHoldSlotRequest>
                {
                    new() { CourtId = 30, StartTime = new TimeOnly(14, 0) }
                }
            };

            // Act
            var result = await _playerBookingService.CreateHolding(req, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.Conflict));
        }

        [Test]
        public async Task CreateHolding_5_WhenPlayerHasScheduleConflict_ReturnsConflict()
        {
            // Arrange
            var user = CreateUser(33, "p33", "p33@test.vn");
            var player = new Player { PlayerId = 33, UserId = 33 };
            var owner = CreateUser(34, "owner34", "owner34@test.vn", "VenueOwner");
            var venueOwner = new VenueOwner { OwnerId = 34, UserId = 34 };
            var venue = CreateVenue(31, 34, "Venue 31", "Hanoi");
            var court = CreateCourt(31, 31, 1, 100000);
            venue.Courts.Add(court);

            var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
            var slotStart = tomorrow.ToDateTime(new TimeOnly(16, 0));
            var slotEnd = tomorrow.ToDateTime(new TimeOnly(17, 0));

            // Player 33 already has a confirmed booking on another court at the same time
            var myOtherBooking = new Booking
            {
                BookingId = 31,
                PlayerId = 33,
                CourtId = 31,
                StartTime = slotStart,
                EndTime = slotEnd,
                Status = "Confirmed",
                CreatedAt = DateTime.UtcNow
            };

            await _dbContext.Users.AddRangeAsync(user, owner);
            await _dbContext.Players.AddAsync(player);
            await _dbContext.VenueOwners.AddAsync(venueOwner);
            await _dbContext.Venues.AddAsync(venue);
            await _dbContext.Bookings.AddAsync(myOtherBooking);
            await _dbContext.SaveChangesAsync();

            _playerBookingService.SetCurrentUserId(33);

            var req = new CreateBookingHoldRequest
            {
                Date = tomorrow,
                Slots = new List<CreateBookingHoldSlotRequest>
                {
                    new() { CourtId = 31, StartTime = new TimeOnly(16, 0) }
                }
            };

            // Act
            var result = await _playerBookingService.CreateHolding(req, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.Conflict));
        }

        [Test]
        public async Task CreateHolding_6_WithValidRequest_CreatesBookingHoldingSuccessfully()
        {
            // Arrange
            var user = CreateUser(35, "p35", "p35@test.vn");
            var player = new Player { PlayerId = 35, UserId = 35 };
            var owner = CreateUser(36, "owner36", "owner36@test.vn", "VenueOwner");
            var venueOwner = new VenueOwner { OwnerId = 36, UserId = 36 };
            var venue = CreateVenue(32, 36, "Clean Venue", "Hanoi");
            var court = CreateCourt(32, 32, 1, 120000);
            venue.Courts.Add(court);

            await _dbContext.Users.AddRangeAsync(user, owner);
            await _dbContext.Players.AddAsync(player);
            await _dbContext.VenueOwners.AddAsync(venueOwner);
            await _dbContext.Venues.AddAsync(venue);
            await _dbContext.SaveChangesAsync();

            _playerBookingService.SetCurrentUserId(35);

            var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

            var req = new CreateBookingHoldRequest
            {
                Date = tomorrow,
                Slots = new List<CreateBookingHoldSlotRequest>
                {
                    new() { CourtId = 32, StartTime = new TimeOnly(18, 0) }
                }
            };

            // Act
            var result = await _playerBookingService.CreateHolding(req, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.Success));
            Assert.That(result.Value, Is.Not.Null);
            Assert.That(result.Value!.Status, Is.EqualTo("Holding"));
            Assert.That(result.Value.TotalAmount, Is.GreaterThan(0));
        }

        #endregion

        #region 5. CompletePayment & RetryPayment Tests (6 test cases)

        [Test]
        public async Task CompletePayment_1_WhenUserNotAuthenticated_ReturnsUnauthorized()
        {
            // Arrange
            _playerBookingService.SetCurrentUserId(null);

            // Act
            var result = await _playerBookingService.CompletePayment(1, new CompleteBookingPaymentRequest(), CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.Unauthorized));
        }

        [Test]
        public async Task CompletePayment_2_WhenBookingNotFound_ReturnsNotFound()
        {
            // Arrange
            _playerBookingService.SetCurrentUserId(1);

            // Act
            var result = await _playerBookingService.CompletePayment(999, new CompleteBookingPaymentRequest(), CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.NotFound));
        }

        [Test]
        public async Task CompletePayment_3_WhenBookingIsNotOwnedByCurrentUser_ReturnsNotFound()
        {
            // Arrange
            var booking = new Booking
            {
                BookingId = 40,
                PlayerId = 1,
                CourtId = 1,
                StartTime = DateTime.UtcNow.AddHours(2),
                EndTime = DateTime.UtcNow.AddHours(3),
                Status = "Holding",
                CreatedAt = DateTime.UtcNow
            };
            await _dbContext.Bookings.AddAsync(booking);
            await _dbContext.SaveChangesAsync();

            _playerBookingService.SetCurrentUserId(2); // Different player

            // Act
            var result = await _playerBookingService.CompletePayment(40, new CompleteBookingPaymentRequest(), CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.NotFound));
        }

        [Test]
        public async Task CompletePayment_4_WhenBookingIsExpiredOrCancelled_ReturnsConflict()
        {
            // Arrange
            var user = CreateUser(41, "p41", "p41@test.vn");
            var player = new Player { PlayerId = 41, UserId = 41 };
            var owner = CreateUser(410, "owner410", "owner410@test.vn", "VenueOwner");
            var venueOwner = new VenueOwner { OwnerId = 410, UserId = 410 };
            var venue = CreateVenue(410, 410, "V410", "Hanoi");
            var court = CreateCourt(410, 410, 1);
            venue.Courts.Add(court);

            var booking = new Booking
            {
                BookingId = 41,
                PlayerId = 41,
                CourtId = 410,
                StartTime = DateTime.UtcNow.AddHours(2),
                EndTime = DateTime.UtcNow.AddHours(3),
                Status = "Cancelled",
                CreatedAt = DateTime.UtcNow
            };
            await _dbContext.Users.AddRangeAsync(user, owner);
            await _dbContext.Players.AddAsync(player);
            await _dbContext.VenueOwners.AddAsync(venueOwner);
            await _dbContext.Venues.AddAsync(venue);
            await _dbContext.Bookings.AddAsync(booking);
            await _dbContext.SaveChangesAsync();

            _playerBookingService.SetCurrentUserId(41);

            // Act
            var result = await _playerBookingService.CompletePayment(41, new CompleteBookingPaymentRequest(), CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.Conflict));
        }

        [Test]
        public async Task CompletePayment_5_WithValidHoldBooking_UpdatesPaymentAndBookingState()
        {
            // Arrange
            var user = CreateUser(42, "p42", "p42@test.vn");
            var player = new Player { PlayerId = 42, UserId = 42 };
            var owner = CreateUser(43, "owner43", "owner43@test.vn", "VenueOwner");
            var venueOwner = new VenueOwner { OwnerId = 43, UserId = 43 };
            var venue = CreateVenue(40, 43, "Payment Venue", "Hanoi");
            var court = CreateCourt(40, 40, 1, 100000);
            venue.Courts.Add(court);

            var booking = new Booking
            {
                BookingId = 42,
                PlayerId = 42,
                CourtId = 40,
                StartTime = DateTime.UtcNow.AddHours(2),
                EndTime = DateTime.UtcNow.AddHours(3),
                Status = "Holding",
                HoldExpiresAt = DateTime.UtcNow.AddMinutes(10),
                TotalAmount = 100000,
                CreatedAt = DateTime.UtcNow
            };

            var payment = new Payment
            {
                PaymentId = 42,
                BookingId = 42,
                PayerId = 42,
                PaymentMethod = "Wallet",
                Amount = 100000,
                Status = "Pending"
            };
            booking.Payments.Add(payment);

            await _dbContext.Users.AddRangeAsync(user, owner);
            await _dbContext.Players.AddAsync(player);
            await _dbContext.VenueOwners.AddAsync(venueOwner);
            await _dbContext.Venues.AddAsync(venue);
            await _dbContext.Bookings.AddAsync(booking);
            await _dbContext.SaveChangesAsync();

            _playerBookingService.SetCurrentUserId(42);

            // Act
            var result = await _playerBookingService.CompletePayment(42, new CompleteBookingPaymentRequest { PaymentMethod = "AtCourt" }, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.Success));
        }

        [Test]
        public async Task RetryPayment_6_WhenBookingNotFoundOrNotPayable_ReturnsError()
        {
            // Arrange
            _playerBookingService.SetCurrentUserId(1);

            // Act
            var result = await _playerBookingService.RetryPayment(999, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.NotFound));
        }

        #endregion

        #region 6. CancelHolding & CancelBooking Tests (6 test cases)

        [Test]
        public async Task CancelHolding_1_WhenUserNotAuthenticated_ReturnsUnauthorized()
        {
            // Arrange
            _playerBookingService.SetCurrentUserId(null);

            // Act
            var result = await _playerBookingService.CancelHolding(1, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.Unauthorized));
        }

        [Test]
        public async Task CancelHolding_2_WhenBookingNotFound_ReturnsNotFound()
        {
            // Arrange
            _playerBookingService.SetCurrentUserId(1);

            // Act
            var result = await _playerBookingService.CancelHolding(999, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.NotFound));
        }

        [Test]
        public async Task CancelHolding_3_WhenBookingNotOwnedByCurrentUser_ReturnsNotFound()
        {
            // Arrange
            var booking = new Booking
            {
                BookingId = 50,
                PlayerId = 1,
                CourtId = 1,
                StartTime = DateTime.UtcNow.AddHours(2),
                EndTime = DateTime.UtcNow.AddHours(3),
                Status = "Holding",
                CreatedAt = DateTime.UtcNow
            };
            await _dbContext.Bookings.AddAsync(booking);
            await _dbContext.SaveChangesAsync();

            _playerBookingService.SetCurrentUserId(2);

            // Act
            var result = await _playerBookingService.CancelHolding(50, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.NotFound));
        }

        [Test]
        public async Task CancelHolding_4_WhenHoldingCancelled_UpdatesStatusToCancelledAndFreesSlots()
        {
            // Arrange
            var user = CreateUser(51, "p51", "p51@test.vn");
            var player = new Player { PlayerId = 51, UserId = 51 };
            var owner = CreateUser(52, "owner52", "owner52@test.vn", "VenueOwner");
            var venueOwner = new VenueOwner { OwnerId = 52, UserId = 52 };
            var venue = CreateVenue(50, 52, "Cancel Venue", "Hanoi");
            var court = CreateCourt(50, 50, 1, 100000);
            venue.Courts.Add(court);

            var booking = new Booking
            {
                BookingId = 51,
                PlayerId = 51,
                CourtId = 50,
                StartTime = DateTime.UtcNow.AddHours(2),
                EndTime = DateTime.UtcNow.AddHours(3),
                Status = "Holding",
                CreatedAt = DateTime.UtcNow
            };

            await _dbContext.Users.AddRangeAsync(user, owner);
            await _dbContext.Players.AddAsync(player);
            await _dbContext.VenueOwners.AddAsync(venueOwner);
            await _dbContext.Venues.AddAsync(venue);
            await _dbContext.Bookings.AddAsync(booking);
            await _dbContext.SaveChangesAsync();

            _playerBookingService.SetCurrentUserId(51);

            // Act
            var result = await _playerBookingService.CancelHolding(51, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.NoContent));
            Assert.That(booking.Status, Is.EqualTo("Cancelled"));
        }

        [Test]
        public async Task CancelBooking_5_WhenBookingIsPastCancellationWindow_ReturnsConflict()
        {
            // Arrange
            var user = CreateUser(53, "p53", "p53@test.vn");
            var player = new Player { PlayerId = 53, UserId = 53 };
            var owner = CreateUser(54, "owner54", "owner54@test.vn", "VenueOwner");
            var venueOwner = new VenueOwner { OwnerId = 54, UserId = 54 };
            var venue = CreateVenue(51, 54, "Past Venue", "Hanoi");
            var court = CreateCourt(51, 51, 1, 100000);
            venue.Courts.Add(court);

            // Booking in the past
            var booking = new Booking
            {
                BookingId = 52,
                PlayerId = 53,
                CourtId = 51,
                StartTime = DateTime.UtcNow.AddHours(-1),
                EndTime = DateTime.UtcNow,
                Status = "Confirmed",
                CreatedAt = DateTime.UtcNow.AddDays(-1)
            };

            await _dbContext.Users.AddRangeAsync(user, owner);
            await _dbContext.Players.AddAsync(player);
            await _dbContext.VenueOwners.AddAsync(venueOwner);
            await _dbContext.Venues.AddAsync(venue);
            await _dbContext.Bookings.AddAsync(booking);
            await _dbContext.SaveChangesAsync();

            _playerBookingService.SetCurrentUserId(53);

            // Act
            var result = await _playerBookingService.CancelBooking(52, new CancelPlayerBookingRequest { Reason = "Busy" }, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.Conflict));
        }

        [Test]
        public async Task CancelBooking_6_WhenCancelBookingValid_CancelsSuccessfully()
        {
            // Arrange
            var user = CreateUser(55, "p55", "p55@test.vn");
            var player = new Player { PlayerId = 55, UserId = 55 };
            var owner = CreateUser(56, "owner56", "owner56@test.vn", "VenueOwner");
            var venueOwner = new VenueOwner { OwnerId = 56, UserId = 56 };
            var venue = CreateVenue(52, 56, "Future Cancel Venue", "Hanoi");
            var court = CreateCourt(52, 52, 1, 100000);
            venue.Courts.Add(court);

            // Booking 3 days in future
            var booking = new Booking
            {
                BookingId = 53,
                PlayerId = 55,
                CourtId = 52,
                StartTime = DateTime.UtcNow.AddDays(3),
                EndTime = DateTime.UtcNow.AddDays(3).AddHours(1),
                Status = "Confirmed",
                CreatedAt = DateTime.UtcNow
            };

            await _dbContext.Users.AddRangeAsync(user, owner);
            await _dbContext.Players.AddAsync(player);
            await _dbContext.VenueOwners.AddAsync(venueOwner);
            await _dbContext.Venues.AddAsync(venue);
            await _dbContext.Bookings.AddAsync(booking);
            await _dbContext.SaveChangesAsync();

            _playerBookingService.SetCurrentUserId(55);

            // Act
            var result = await _playerBookingService.CancelBooking(53, new CancelPlayerBookingRequest { Reason = "Change of plan" }, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.NoContent));
            Assert.That(booking.Status, Is.EqualTo("Cancelled"));
        }

        #endregion

        #region 7. GetMyBookings & GetBooking Tests (6 test cases)

        [Test]
        public async Task GetMyBookings_1_WhenUserNotAuthenticated_ReturnsUnauthorized()
        {
            // Arrange
            _playerBookingService.SetCurrentUserId(null);

            // Act
            var result = await _playerBookingService.GetMyBookings(1, 10, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.Unauthorized));
        }

        [Test]
        public async Task GetMyBookings_2_WhenUserHasNoBookings_ReturnsEmptyPaginatedList()
        {
            // Arrange
            _playerBookingService.SetCurrentUserId(60);

            // Act
            var result = await _playerBookingService.GetMyBookings(1, 10, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.Success));
            Assert.That(result.Value!.TotalCount, Is.EqualTo(0));
            Assert.That(result.Value.Items, Is.Empty);
        }

        [Test]
        public async Task GetBooking_3_WhenUserNotAuthenticated_ReturnsNotFound()
        {
            // Arrange
            _playerBookingService.SetCurrentUserId(null);

            // Act
            var result = await _playerBookingService.GetBooking(1, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.NotFound));
        }

        [Test]
        public async Task GetBooking_4_WhenBookingNotFound_ReturnsNotFound()
        {
            // Arrange
            _playerBookingService.SetCurrentUserId(1);

            // Act
            var result = await _playerBookingService.GetBooking(999, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.NotFound));
        }

        [Test]
        public async Task GetBooking_5_WhenBookingBelongsToOtherUser_ReturnsNotFound()
        {
            // Arrange
            var user1 = CreateUser(70, "p70", "p70@test.vn");
            var player1 = new Player { PlayerId = 70, UserId = 70 };
            var user2 = CreateUser(71, "p71", "p71@test.vn");
            var player2 = new Player { PlayerId = 71, UserId = 71 };

            var booking = new Booking
            {
                BookingId = 70,
                PlayerId = 70, // belongs to player 70
                CourtId = 1,
                StartTime = DateTime.UtcNow.AddHours(2),
                EndTime = DateTime.UtcNow.AddHours(3),
                Status = "Confirmed",
                CreatedAt = DateTime.UtcNow
            };

            await _dbContext.Users.AddRangeAsync(user1, user2);
            await _dbContext.Players.AddRangeAsync(player1, player2);
            await _dbContext.Bookings.AddAsync(booking);
            await _dbContext.SaveChangesAsync();

            _playerBookingService.SetCurrentUserId(71); // player 71 requests

            // Act
            var result = await _playerBookingService.GetBooking(70, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.NotFound));
        }

        [Test]
        public async Task GetBooking_6_WhenBookingExistsAndOwned_ReturnsBookingDetail()
        {
            // Arrange
            var user = CreateUser(72, "p72", "p72@test.vn");
            var player = new Player { PlayerId = 72, UserId = 72 };
            var owner = CreateUser(73, "owner73", "owner73@test.vn", "VenueOwner");
            var venueOwner = new VenueOwner { OwnerId = 73, UserId = 73 };
            var venue = CreateVenue(70, 73, "Detailed Venue", "Hanoi");
            var court = CreateCourt(70, 70, 1, 100000);
            venue.Courts.Add(court);

            var booking = new Booking
            {
                BookingId = 72,
                PlayerId = 72,
                CourtId = 70,
                StartTime = DateTime.UtcNow.AddHours(2),
                EndTime = DateTime.UtcNow.AddHours(3),
                Status = "Confirmed",
                TotalAmount = 100000,
                CreatedAt = DateTime.UtcNow
            };

            await _dbContext.Users.AddRangeAsync(user, owner);
            await _dbContext.Players.AddAsync(player);
            await _dbContext.VenueOwners.AddAsync(venueOwner);
            await _dbContext.Venues.AddAsync(venue);
            await _dbContext.Bookings.AddAsync(booking);
            await _dbContext.SaveChangesAsync();

            _playerBookingService.SetCurrentUserId(72);

            // Act
            var result = await _playerBookingService.GetBooking(72, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(ServiceResultStatus.Success));
            Assert.That(result.Value!.BookingId, Is.EqualTo(72));
            Assert.That(result.Value.Status, Is.EqualTo("Confirmed"));
        }

        #endregion
    }
}
