namespace PicklinkBackend.Tests;

public class LocationsApiContractTests
{
    [Fact]
    public void LocationsApiExposesProvinceAndWardDropdownEndpoints()
    {
        var controller = File.ReadAllText(SourcePath("Controllers", "Locations", "LocationsController.cs"));
        var service = File.ReadAllText(SourcePath("Services", "Locations", "LocationQueryService.cs"));
        var dtos = File.ReadAllText(SourcePath("DTOs", "LocationDtos.cs"));

        Assert.Contains("[ApiController]", controller);
        Assert.Contains("[Route(\"api/locations\")]", controller);
        Assert.Contains("[HttpGet(\"provinces\")]", controller);
        Assert.Contains("[HttpGet(\"provinces/{provinceCode}/wards\")]", controller);
        Assert.Contains("LocationQueryService", controller);
        Assert.Contains("ProvinceResponse", controller);
        Assert.Contains("WardResponse", controller);
        Assert.Contains("_venueRepository.ListProvincesAsync", service);
        Assert.Contains("_venueRepository.ListWardsAsync", service);
        Assert.Contains("ProvinceResponse", dtos);
        Assert.Contains("WardResponse", dtos);
    }

    [Fact]
    public void LocationsApiDependenciesAreRegistered()
    {
        var source = File.ReadAllText(SourcePath("Startup", "ServiceRegistration.cs"));

        Assert.Contains("services.AddHttpClient", source);
        Assert.Contains("services.AddMemoryCache", source);
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
