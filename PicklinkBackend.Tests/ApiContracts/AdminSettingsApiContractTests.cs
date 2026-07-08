namespace PicklinkBackend.Tests;

public class AdminSettingsApiContractTests
{
    [Fact]
    public void PlatformSettingModelAndSchemaAreRegistered()
    {
        var model = File.ReadAllText(SourcePath("Models", "PlatformSetting.cs"));
        var dbContext = File.ReadAllText(SourcePath("Data", "ApplicationDbContext.cs"));
        var schemaStartup = File.ReadAllText(SourcePath("Startup", "SchemaStartup.cs"));

        Assert.Contains("public string SettingKey { get; set; }", model);
        Assert.Contains("public string SettingValue { get; set; }", model);
        Assert.Contains("public int? UpdatedByUserId { get; set; }", model);
        Assert.Contains("DbSet<PlatformSetting>", dbContext);
        Assert.Contains("PLATFORM_SETTING", dbContext);
        Assert.Contains("EnsureAdminSettingsSchema(app)", schemaStartup);
        Assert.Contains("CREATE TABLE [PLATFORM_SETTING]", schemaStartup);
        Assert.DoesNotContain("Tournament", model);
    }

    [Fact]
    public void AdminCanReadAndUpdateRealSettings()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "Admin", "AdminSettingsController.cs"));
        var service = File.ReadAllText(SourcePath("Services", "Admin", "AdminSettingService.cs"));
        var dtos = File.ReadAllText(SourcePath("DTOs", "AdminSettingDtos.cs"));

        Assert.Contains("[Authorize(Roles = \"Admin\")]", source);
        Assert.Contains("[Route(\"api/admin/settings\")]", source);
        Assert.Contains("[HttpGet]", source);
        Assert.Contains("[HttpPut(\"{settingKey}\")]", source);
        Assert.Contains("AdminSettingService", source);
        Assert.DoesNotContain("ApplicationDbContext", source);
        Assert.DoesNotContain("public sealed class AdminSettingResponse", source);
        Assert.Contains("_dbContext.PlatformSettings", service);
        Assert.Contains("bookingHoldMinutes", service);
        Assert.Contains("listingExpiryReminderDays", service);
        Assert.Contains("maxReceiptUploadMb", service);
        Assert.Contains("AdminSettingUpdateRequest", dtos);
        Assert.Contains("AdminSettingResponse", dtos);
        Assert.DoesNotContain("Tournament", source);
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
