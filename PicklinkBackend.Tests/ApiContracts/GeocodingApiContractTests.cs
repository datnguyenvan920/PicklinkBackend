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
        Assert.Contains("format=jsonv2", service);
        Assert.Contains("ParseJsonV2ReverseResult", service);
        Assert.Contains("FirstNonEmpty(address", service);
        Assert.Contains("results.Count == 5", service);
        Assert.Contains("AddSingleton<GeocodingService>()", registration);
        Assert.Contains("NominatimBaseUrl", settings);
    }

    private static string SourcePath(params string[] relativeSegments)
    {
        var cleanSegments = relativeSegments.FirstOrDefault() == "PicklinkBackend" ? relativeSegments[1..] : relativeSegments;
        var fileName = cleanSegments.Last();
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var projectDir = Path.Combine(directory.FullName, "PicklinkBackend");
            if (Directory.Exists(projectDir))
            {
                var candidate = Path.Combine([projectDir, .. cleanSegments]);
                if (File.Exists(candidate) || Directory.Exists(candidate)) return candidate;

                var foundFile = Directory.GetFiles(projectDir, fileName, SearchOption.AllDirectories).FirstOrDefault();
                if (foundFile is not null) return foundFile;

                var foundDir = Directory.GetDirectories(projectDir, fileName, SearchOption.AllDirectories).FirstOrDefault();
                if (foundDir is not null) return foundDir;
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {string.Join('/', relativeSegments)}.");
    }
}
