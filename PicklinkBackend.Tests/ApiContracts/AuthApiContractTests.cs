namespace PicklinkBackend.Tests.ApiContracts;

public class AuthApiContractTests
{
    [Fact]
    public void AuthControllerDelegatesToAuthService()
    {
        var controllerDirectory = Path.GetDirectoryName(SourcePath("Controllers", "Auth", "AuthController.cs"))
            ?? throw new DirectoryNotFoundException("Could not locate Auth controller directory.");
        var controllers = string.Join(
            Environment.NewLine,
            Directory.GetFiles(controllerDirectory, "AuthController*.cs").Select(File.ReadAllText));
        var service = File.ReadAllText(SourcePath("Services", "AuthService.cs"));
        var services = File.ReadAllText(SourcePath("Startup", "ServiceRegistration.cs"));

        Assert.Contains("AuthService", controllers);
        Assert.Contains("services.AddScoped<AuthService>()", services);
        Assert.DoesNotContain("ApplicationDbContext", controllers);
        Assert.DoesNotContain("_dbContext", controllers);
        Assert.Contains("public async Task<AuthServiceResult<AuthResponse>> RegisterAsync", service);
        Assert.Contains("public async Task<AuthServiceResult<AuthResponse>> LoginAsync", service);
        Assert.Contains("if (user.IsLocked)", service);
        Assert.Contains("HashPasswordResetToken", service);
        Assert.Contains("MapExperienceToSkillLevel", service);
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
