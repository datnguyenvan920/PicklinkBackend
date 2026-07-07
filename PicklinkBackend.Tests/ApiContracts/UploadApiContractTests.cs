namespace PicklinkBackend.Tests;

public class UploadApiContractTests
{
    [Fact]
    public void UploadControllerDelegatesCloudinarySignatureGeneration()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "Venues", "UploadController.cs"));
        var service = File.ReadAllText(SourcePath("Services", "CloudinarySignatureService.cs"));
        var dtos = File.ReadAllText(SourcePath("DTOs", "CloudinaryDtos.cs"));
        var services = File.ReadAllText(SourcePath("Startup", "ServiceRegistration.cs"));

        Assert.Contains("[Authorize]", source);
        Assert.Contains("[Route(\"api/[controller]\")]", source);
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