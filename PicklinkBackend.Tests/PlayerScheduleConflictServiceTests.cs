using Microsoft.EntityFrameworkCore;
using PicklinkBackend.Data;
using PicklinkBackend.Models;
using PicklinkBackend.Services;
using Xunit;

namespace PicklinkBackend.Tests;

public class PlayerScheduleConflictServiceTests
{
    private static readonly DateTime SlotStart = new(2026, 7, 1, 18, 0, 0);
    private static readonly DateTime SlotEnd = SlotStart.AddHours(2);

    [Fact]
    public async Task DirectBooking_OverlappingTime_HasConflict()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Bookings.Add(CreateBooking(playerId: 10, status: "Confirmed"));
        await dbContext.SaveChangesAsync();

        var service = new PlayerScheduleConflictService(dbContext);

        Assert.True(await service.HasConflictAsync(10, SlotStart.AddMinutes(30), SlotEnd.AddMinutes(30)));
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Accepted")]
    public async Task ActiveMatchParticipant_OverlappingTime_HasConflict(string participantStatus)
    {
        await using var dbContext = CreateDbContext();
        AddMatchBooking(dbContext, playerId: 20, participantStatus);
        await dbContext.SaveChangesAsync();

        var service = new PlayerScheduleConflictService(dbContext);

        Assert.True(await service.HasConflictAsync(20, SlotStart.AddMinutes(30), SlotEnd.AddMinutes(30)));
    }

    [Theory]
    [InlineData("Rejected")]
    [InlineData("Left")]
    [InlineData("Removed")]
    public async Task InactiveMatchParticipant_OverlappingTime_DoesNotConflict(string participantStatus)
    {
        await using var dbContext = CreateDbContext();
        AddMatchBooking(dbContext, playerId: 30, participantStatus);
        await dbContext.SaveChangesAsync();

        var service = new PlayerScheduleConflictService(dbContext);

        Assert.False(await service.HasConflictAsync(30, SlotStart.AddMinutes(30), SlotEnd.AddMinutes(30)));
    }

    [Theory]
    [InlineData("Cancelled")]
    [InlineData("Expired")]
    public async Task InactiveBooking_OverlappingTime_DoesNotConflict(string bookingStatus)
    {
        await using var dbContext = CreateDbContext();
        dbContext.Bookings.Add(CreateBooking(playerId: 40, status: bookingStatus));
        await dbContext.SaveChangesAsync();

        var service = new PlayerScheduleConflictService(dbContext);

        Assert.False(await service.HasConflictAsync(40, SlotStart.AddMinutes(30), SlotEnd.AddMinutes(30)));
    }

    [Fact]
    public async Task ExpiredHolding_OverlappingTime_DoesNotConflict()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Bookings.Add(CreateBooking(
            playerId: 50,
            status: "Holding",
            holdExpiresAt: DateTime.UtcNow.AddMinutes(-1)));
        await dbContext.SaveChangesAsync();

        var service = new PlayerScheduleConflictService(dbContext);

        Assert.False(await service.HasConflictAsync(50, SlotStart.AddMinutes(30), SlotEnd.AddMinutes(30)));
    }

    [Fact]
    public async Task AdjacentTime_DoesNotConflict()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Bookings.Add(CreateBooking(playerId: 60, status: "Confirmed"));
        await dbContext.SaveChangesAsync();

        var service = new PlayerScheduleConflictService(dbContext);

        Assert.False(await service.HasConflictAsync(60, SlotEnd, SlotEnd.AddHours(1)));
    }

    [Fact]
    public async Task ExcludedMatch_DoesNotConflictWithItself()
    {
        await using var dbContext = CreateDbContext();
        var match = AddMatchBooking(dbContext, playerId: 70, participantStatus: "Pending");
        await dbContext.SaveChangesAsync();

        var service = new PlayerScheduleConflictService(dbContext);

        Assert.False(await service.HasConflictAsync(
            70,
            SlotStart,
            SlotEnd,
            excludedMatchId: match.MatchId));
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static Booking CreateBooking(
        int playerId,
        string status,
        DateTime? holdExpiresAt = null) => new()
    {
        PlayerId = playerId,
        CourtId = 1,
        StartTime = SlotStart,
        EndTime = SlotEnd,
        Status = status,
        CreatedAt = DateTime.UtcNow,
        HoldExpiresAt = holdExpiresAt
    };

    private static Match AddMatchBooking(
        ApplicationDbContext dbContext,
        int playerId,
        string participantStatus)
    {
        var match = new Match
        {
            HostPlayerId = 999,
            MatchType = "2vs2",
            MatchSkillLevel = 3,
            RequiredPlayerCount = 4,
            MatchTime = SlotStart,
            Status = "Waiting",
            CreatedAt = DateTime.UtcNow
        };
        match.Bookings.Add(new Booking
        {
            PlayerId = 999,
            CourtId = 1,
            StartTime = SlotStart,
            EndTime = SlotEnd,
            Status = "MatchWaiting",
            CreatedAt = DateTime.UtcNow,
            HoldExpiresAt = DateTime.UtcNow.AddMinutes(10)
        });
        match.MatchParticipants.Add(new MatchParticipant
        {
            PlayerId = playerId,
            Status = participantStatus,
            RequestedAt = DateTime.UtcNow
        });
        dbContext.Matches.Add(match);
        return match;
    }
}
