using PicklinkBackend.Services.Ticketing;

namespace PicklinkBackend.Tests.Services;

public class TicketingPolicyTests
{
    private static readonly DateTime UtcNow = new(2026, 7, 18, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CapacityCountsPaidAndLiveHoldsButReleasesExpiredHolds()
    {
        Assert.True(TicketingPolicy.OccupiesCapacity("Paid", null, UtcNow));
        Assert.True(TicketingPolicy.OccupiesCapacity("CheckedIn", null, UtcNow));
        Assert.True(TicketingPolicy.OccupiesCapacity("PendingPayment", UtcNow.AddMinutes(1), UtcNow));
        Assert.False(TicketingPolicy.OccupiesCapacity("PendingPayment", UtcNow, UtcNow));
        Assert.False(TicketingPolicy.OccupiesCapacity("RefundPending", null, UtcNow));
        Assert.Equal("Expired", TicketingPolicy.EffectiveTicketStatus("PendingPayment", UtcNow, UtcNow));
        Assert.Equal("Expired", TicketingPolicy.EffectiveTicketStatus("PendingPayment", null, UtcNow));
    }

    [Fact]
    public void PlayerCancellationHonorsTheStoredDeadline()
    {
        var start = new DateTime(2026, 7, 20, 18, 0, 0);

        Assert.True(TicketingPolicy.CanPlayerCancel(start, start.AddHours(-24), 24));
        Assert.False(TicketingPolicy.CanPlayerCancel(start, start.AddHours(-24).AddSeconds(1), 24));
    }

    [Fact]
    public void CheckInOnlyOpensInsideTheConfiguredWindow()
    {
        var start = new DateTime(2026, 7, 20, 18, 0, 0);
        var end = start.AddHours(2);

        Assert.False(TicketingPolicy.CanCheckIn(start, end, start.AddMinutes(-31), 30));
        Assert.True(TicketingPolicy.CanCheckIn(start, end, start.AddMinutes(-30), 30));
        Assert.True(TicketingPolicy.CanCheckIn(start, end, end, 30));
        Assert.False(TicketingPolicy.CanCheckIn(start, end, end.AddSeconds(1), 30));
    }
}
