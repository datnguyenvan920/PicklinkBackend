using PicklinkBackend.DTOs;
using PicklinkBackend.Services.Bookings;

namespace PicklinkBackend.Tests.Services;

public class PlayerBookingReviewResultTests
{
    [Fact]
    public void CreatedPreservesThePersistedRatingIdentity()
    {
        var createdAt = new DateTime(2026, 8, 11, 15, 31, 24, DateTimeKind.Utc);
        var response = new BookingReviewResponse
        {
            RatingId = 27,
            BookingId = 8156,
            VenueId = 11,
            VenueName = "Sân QL21",
            Score = 5,
            Comment = "Sân tốt",
            Tags = ["Sạch sẽ"],
            IsAnonymous = false,
            CreatedAt = createdAt
        };

        var result = PlayerBookingReviewResult.Created(response);

        Assert.Equal(PlayerBookingReviewResultStatus.Created, result.Status);
        Assert.NotNull(result.Value);
        Assert.Equal(27, result.Value.RatingId);
        Assert.Equal(8156, result.Value.BookingId);
        Assert.Equal(11, result.Value.VenueId);
        Assert.Equal("Sân QL21", result.Value.VenueName);
        Assert.Equal(5, result.Value.Score);
        Assert.Equal("Sân tốt", result.Value.Comment);
        Assert.Equal(new[] { "Sạch sẽ" }, result.Value.Tags);
        Assert.False(result.Value.IsAnonymous);
        Assert.Equal(createdAt, result.Value.CreatedAt);
    }
}
