namespace PicklinkBackend.Tests;

public class GeocodingApiContractTests
{
    [Fact]
    public void LocationControllerExposesValidatedForwardReverseAndSearchRoutes()
    {
        var controller = File.ReadAllText(SourcePath("Controllers", "Locations", "LocationsController.cs"));
        var dtos = File.ReadAllText(SourcePath("DTOs", "LocationDtos.cs"));

        Assert.Contains("""[HttpGet("geocode/forward")]""", controller);
        Assert.Contains("""[HttpGet("geocode/reverse")]""", controller);
        Assert.Contains("""[HttpGet("geocode/search")]""", controller);
        Assert.Contains("double.IsFinite(latitude)", controller);
        Assert.Contains("query.Length is < 3 or > 200", controller);
        Assert.Contains("StatusCodes.Status502BadGateway", controller);
        Assert.Contains("GeocodeCoordinatesResponse", dtos);
        Assert.Contains("ReverseGeocodeResponse", dtos);
        Assert.Contains("GeocodingSearchResultResponse", dtos);
    }

    [Fact]
    public void ProxyCachesAndGloballyThrottlesVietnamOnlyProviderCalls()
    {
        var service = File.ReadAllText(SourcePath("Services", "Locations", "GeocodingService.cs"));
        var registration = File.ReadAllText(SourcePath("Startup", "ServiceRegistration.cs"));
        var settings = File.ReadAllText(SourcePath("appsettings.json"));

        Assert.Contains("Geocoding:NominatimBaseUrl", service);
        Assert.Contains("PicklinkBackend/1.0", service);
        Assert.Contains("TimeSpan.FromSeconds(1)", service);
        Assert.Contains("SemaphoreSlim", service);
        Assert.Contains("IMemoryCache", service);
        Assert.Contains("countrycodes=vn", service);
        Assert.Contains("results.Count == 5", service);
        Assert.Contains("AddSingleton<GeocodingService>()", registration);
        Assert.Contains("NominatimBaseUrl", settings);
    }

    private static string SourcePath(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                new[] { directory.FullName, "PicklinkBackend" }.Concat(relativeSegments).ToArray());
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {string.Join('/', relativeSegments)}.");
    }
}
