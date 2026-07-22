namespace PicklinkBackend.Tests;

public class UploadApiContractTests
{
    [Fact]
    public void UploadControllerDelegatesCloudinarySignatureGeneration()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "Venues", "UploadController.cs"));
        var service = File.ReadAllText(SourcePath("Services", "Venues", "CloudinarySignatureService.cs"));
        var dtos = File.ReadAllText(SourcePath("DTOs", "CloudinaryDtos.cs"));
        var services = File.ReadAllText(SourcePath("Startup", "ServiceRegistration.cs"));

        Assert.Contains("[Authorize]", source);
        Assert.Contains("[Route(\"api/upload\")]", source);
        Assert.Contains("[HttpPost(\"signature\")]", source);
        Assert.Contains("CloudinarySignatureService", source);
        Assert.Contains("services.AddScoped<CloudinarySignatureService>()", services);
        Assert.DoesNotContain("IConfiguration", source);
        Assert.DoesNotContain("SHA1.HashData", source);
        Assert.DoesNotContain("public class SignatureRequest", source);
        Assert.Contains("CloudinarySignaturePolicy.TryValidate", service);
        Assert.Contains("SHA1.HashData", service);
        Assert.Contains("public class SignatureRequest", dtos);
        Assert.Contains("public class SignatureResponse", dtos);
    }

    [Fact]
    public void UploadControllerProvidesLocalClubCoverFallbackWhenCloudinaryIsNotConfigured()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "Venues", "UploadController.cs"));
        var service = File.ReadAllText(SourcePath("Services", "Venues", "LocalUploadService.cs"));
        var dtos = File.ReadAllText(SourcePath("DTOs", "CloudinaryDtos.cs"));
        var services = File.ReadAllText(SourcePath("Startup", "ServiceRegistration.cs"));
        var uploadStartup = File.ReadAllText(SourcePath("Startup", "UploadDirectoryStartup.cs"));

        Assert.Contains("LocalUploadService", source);
        Assert.Contains("[HttpPost(\"club-cover\")]", source);
        Assert.Contains("UploadClubCover", source);
        Assert.Contains("SaveClubCoverAsync", service);
        Assert.Contains("\"uploads\", \"group-covers\"", service);
        Assert.Contains("LocalUploadResponse", dtos);
        Assert.Contains("services.AddScoped<LocalUploadService>()", services);
        Assert.Contains("\"uploads\", \"group-covers\"", uploadStartup);
        Assert.DoesNotContain("File.Create", source);
    }

    [Fact]
    public void UploadControllerProvidesMediaDeletionEndpoint()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "Venues", "UploadController.cs"));
        var service = File.ReadAllText(SourcePath("Services", "Venues", "LocalUploadService.cs"));
        var dtos = File.ReadAllText(SourcePath("DTOs", "CloudinaryDtos.cs"));

        Assert.Contains("[HttpPost(\"delete\")]", source);
        Assert.Contains("DeleteUploadedMedia", source);
        Assert.Contains("DeleteMediaAsync", service);
        Assert.Contains("DeleteUploadRequest", dtos);
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
