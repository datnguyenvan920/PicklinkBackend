namespace PicklinkBackend.Tests;

public class LocationsApiContractTests
{
    [Fact]
    public void LocationsApiExposesProvinceAndWardDropdownEndpoints()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "Locations", "LocationsController.cs"));

        Assert.Contains("[ApiController]", source);
        Assert.Contains("[Route(\"api/locations\")]", source);
        Assert.Contains("[HttpGet(\"provinces\")]", source);
        Assert.Contains("[HttpGet(\"provinces/{provinceCode}/wards\")]", source);
        Assert.Contains("ProvinceOption", source);
        Assert.Contains("WardOption", source);
        Assert.Contains("https://provinces.open-api.vn/api/v2/p/", source);
        Assert.Contains("GetFromJsonAsync", source);
        Assert.Contains("_cache", source);
        Assert.Contains("province.Code.ToString", source);
        Assert.Contains("ward.ProvinceCode.ToString", source);
        Assert.Contains("RemoveAdministrativePrefix", source);
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
