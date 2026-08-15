namespace PicklinkBackend.Tests;

public class MyBookingsOrderingContractTests
{
    [Fact]
    public void MyBookingsOnlyIncludePaymentsSubmittedByTheCurrentPlayer()
    {
        var source = File.ReadAllText(SourcePath("Repositories", "Implementations", "BookingRepository.cs"));

        Assert.Contains("payment.Payer.UserId == userId", source);
        Assert.Contains("payment.SubmittedAt.HasValue || payment.PaidAt.HasValue", source);
    }

    [Fact]
    public void MyBookingsAreOrderedByNewestCreationTimeBeforePagination()
    {
        var source = File.ReadAllText(SourcePath("Services", "Bookings", "Implementations", "PlayerBookingService.cs"));
        var start = source.IndexOf("GetMyBookings(", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = source.IndexOf("public async Task<ServiceResult<BookingHoldingResponse>> GetBooking", start, StringComparison.Ordinal);
        Assert.True(end > start);
        var method = source[start..end];
        var order = method.IndexOf(".OrderByDescending(booking => booking.CreatedAt)", StringComparison.Ordinal);
        var tieBreak = method.IndexOf(".ThenByDescending(booking => booking.BookingId)", StringComparison.Ordinal);
        var skip = method.IndexOf(".Skip((page - 1) * pageSize)", StringComparison.Ordinal);

        Assert.True(order >= 0);
        Assert.True(tieBreak > order);
        Assert.True(skip > tieBreak);
        Assert.DoesNotContain(".OrderByDescending(booking => booking.StartTime)", method);
    }

    private static string SourcePath(params string[] relativeSegments)
    {
        var fileName = relativeSegments.Last();
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var projectDir = Path.Combine(directory.FullName, "PicklinkBackend");
            if (Directory.Exists(projectDir))
            {
                var candidate = Path.Combine([projectDir, .. relativeSegments]);
                if (File.Exists(candidate)) return candidate;

                var foundFile = Directory.GetFiles(projectDir, fileName, SearchOption.AllDirectories).FirstOrDefault();
                if (foundFile is not null) return foundFile;
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {string.Join('/', relativeSegments)}.");
    }
}
