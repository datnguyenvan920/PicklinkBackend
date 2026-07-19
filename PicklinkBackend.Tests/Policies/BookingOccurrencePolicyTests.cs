using PicklinkBackend.Services.Bookings;

namespace PicklinkBackend.Tests.Policies;

public class BookingOccurrencePolicyTests
{
    private static readonly DateTime DayOne = new(2026, 7, 20, 9, 0, 0);
    private static readonly DateTime DayTwo = new(2026, 7, 22, 9, 0, 0);

    [Theory]
    [InlineData(2026, 7, 21, 12, 0, "NotOpen")]
    [InlineData(2026, 7, 22, 8, 45, "Ready")]
    public void MultiDayStatusUsesTheActualOccurrence(int year, int month, int day, int hour, int minute, string expected)
    {
        var occurrences = new[]
        {
            new BookingOccurrence(DayOne, DayOne.AddHours(1), "CheckedIn"),
            new BookingOccurrence(DayTwo, DayTwo.AddHours(1), "Ready")
        };

        var status = BookingOccurrencePolicy.GetCheckInStatus(
            "Confirmed",
            "Ready",
            occurrences,
            new DateTime(year, month, day, hour, minute, 0),
            DayOne,
            DayTwo.AddHours(1));

        Assert.Equal(expected, status);
    }

    [Fact]
    public void CompletedOccurrencesAggregateOnlyAfterEveryDayIsProcessed()
    {
        var status = BookingOccurrencePolicy.GetCheckInStatus(
            "Confirmed",
            "Ready",
            new[]
            {
                new BookingOccurrence(DayOne, DayOne.AddHours(1), "CheckedIn"),
                new BookingOccurrence(DayTwo, DayTwo.AddHours(1), "NoShow")
            },
            DayTwo.AddHours(2),
            DayOne,
            DayTwo.AddHours(1));

        Assert.Equal("CheckedIn", status);
    }
}
