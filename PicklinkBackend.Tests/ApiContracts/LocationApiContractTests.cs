namespace PicklinkBackend.Tests;

public class LocationApiContractTests
{
    [Fact]
    public void LocationModelsSchemaAndSeedAreRegistered()
    {
        var provinceModel = File.ReadAllText(SourcePath("Models", "Province.cs"));
        var wardModel = File.ReadAllText(SourcePath("Models", "Ward.cs"));
        var dbContext = File.ReadAllText(SourcePath("Data", "ApplicationDbContext.cs"));
        var schemaStartup = File.ReadAllText(SourcePath("Startup", "SchemaStartup.cs"));
        var seed = File.ReadAllText(SeedPath("seed_vietnam_administrative_units_2025.sql"));
        var sourceCsv = File.ReadAllText(SeedPath("tinh_xa_phuong_chi_ten_2025.csv"));

        Assert.Contains("public string Code { get; set; }", provinceModel);
        Assert.Contains("public virtual ICollection<Ward> Wards { get; set; }", provinceModel);
        Assert.DoesNotContain("Type", provinceModel);
        Assert.DoesNotContain("TaxCode", provinceModel);
        Assert.Contains("public string ProvinceCode { get; set; }", wardModel);
        Assert.Contains("public virtual Province Province { get; set; }", wardModel);
        Assert.DoesNotContain("Type", wardModel);
        Assert.DoesNotContain("OldDistrict", wardModel);
        Assert.Contains("DbSet<Province>", dbContext);
        Assert.Contains("DbSet<Ward>", dbContext);
        Assert.Contains("ToTable(\"Provinces\")", dbContext);
        Assert.Contains("ToTable(\"Wards\")", dbContext);
        Assert.DoesNotContain("TaxCode", dbContext);
        Assert.DoesNotContain("OldDistrict", dbContext);
        Assert.Contains("EnsureLocationSchema(app)", schemaStartup);
        Assert.Contains("CREATE TABLE [Provinces]", schemaStartup);
        Assert.Contains("CREATE TABLE [Wards]", schemaStartup);
        Assert.DoesNotContain("[Type] nvarchar", schemaStartup);
        Assert.DoesNotContain("[TaxCode] nvarchar", schemaStartup);
        Assert.DoesNotContain("[OldDistrictTaxCode] nvarchar", schemaStartup);
        Assert.DoesNotContain("[OldDistrictName] nvarchar", schemaStartup);
        Assert.Contains("P001-W001", schemaStartup);
        Assert.Contains("seed_vietnam_administrative_units_2025.sql", schemaStartup);
        Assert.Contains("Generated from tinh_xa_phuong_chi_ten_2025.csv", seed);
        Assert.Contains("DELETE FROM dbo.Wards", seed);
        Assert.Contains("DELETE FROM dbo.Provinces", seed);
        Assert.Contains("INSERT INTO dbo.Provinces", seed);
        Assert.Contains("INSERT INTO dbo.Wards", seed);
        Assert.DoesNotContain("TaxCode NVARCHAR", seed);
        Assert.DoesNotContain("OldDistrictTaxCode NVARCHAR", seed);
        Assert.DoesNotContain("OldDistrictName NVARCHAR", seed);
        Assert.Contains("N'P001'", seed);
        Assert.Contains("N'P001-W001'", seed);
        Assert.Contains("TenTinhThanh,TenXaPhuong", sourceCsv);
        Assert.Contains("T\u1ec9nh H\u01b0ng Y\u00ean,X\u00e3 Ngh\u0129a Tr\u1ee5", sourceCsv);
    }

    [Fact]
    public void PublicLocationApiUsesServiceAndDtoContracts()
    {
        var controller = File.ReadAllText(SourcePath("Controllers", "Locations", "LocationsController.cs"));
        var service = File.ReadAllText(SourcePath("Services", "Locations", "LocationQueryService.cs"));
        var dtos = File.ReadAllText(SourcePath("DTOs", "LocationDtos.cs"));
        var serviceRegistration = File.ReadAllText(SourcePath("Startup", "ServiceRegistration.cs"));

        Assert.Contains("[Route(\"api/locations\")]", controller);
        Assert.Contains("[HttpGet(\"provinces\")]", controller);
        Assert.Contains("[HttpGet(\"provinces/{provinceCode}/wards\")]", controller);
        Assert.Contains("LocationQueryService", controller);
        Assert.DoesNotContain("ApplicationDbContext", controller);
        Assert.DoesNotContain("[Authorize", controller);
        Assert.Contains("_dbContext.Provinces", service);
        Assert.Contains("_dbContext.Wards", service);
        Assert.Contains("AsNoTracking()", service);
        Assert.Contains("OrderBy(province => province.Code)", service);
        Assert.Contains("OrderBy(ward => ward.Code)", service);
        Assert.Contains("ProvinceResponse", dtos);
        Assert.Contains("WardResponse", dtos);
        Assert.DoesNotContain("Type", dtos);
        Assert.Contains("AddScoped<LocationQueryService>()", serviceRegistration);
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

    private static string SeedPath(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "database", "seeds", fileName);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate database/seeds/{fileName}.");
    }
}