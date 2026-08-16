namespace PicklinkBackend.Tests.ApiContracts;

public class CheckoutMatchBookingContractTests
{
    [Fact]
    public void CheckoutLookupReturnsOnlyTheCurrentPlayersMatchId()
    {
        var dto = File.ReadAllText(SourcePath("DTOs", "PaymentDtos.cs"));
        var controller = File.ReadAllText(SourcePath("Controllers", "Payments", "PaymentController.cs"));
        var service = File.ReadAllText(SourcePath("Services", "Payments", "Implementations", "PaymentService.cs"));

        Assert.Contains("public class CheckoutBookingContextResponse", dto);
        Assert.Contains("checkout-context", controller);
        Assert.Contains("item.Payer.UserId == userId.Value", service);
        Assert.Contains("MatchId = item.Booking.MatchId", service);
    }

    private static string SourcePath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var projectDir = Path.Combine(directory.FullName, "PicklinkBackend");
            if (Directory.Exists(projectDir))
            {
                var candidate = Path.Combine([projectDir, .. segments]);
                if (File.Exists(candidate)) return candidate;

                var fallback = Directory.GetFiles(projectDir, segments.Last(), SearchOption.AllDirectories).FirstOrDefault();
                if (fallback is not null) return fallback;
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {string.Join('/', segments)}.");
    }
}
