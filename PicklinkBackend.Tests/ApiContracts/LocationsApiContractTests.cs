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
        Assert.Contains("_dbContext.Provinces", service);
        Assert.Contains("_dbContext.Wards", service);
        Assert.Contains("OrderBy(province => province.Code)", service);
        Assert.Contains("OrderBy(ward => ward.Code)", service);
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
