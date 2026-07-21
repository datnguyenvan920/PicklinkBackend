namespace PicklinkBackend.Tests.Security;

public sealed class AuthHardeningContractTests
{
    [Fact]
    public void ExistingJwtIsRejectedWhenAccountIsLockedOrMissing()
    {
        var registration = Source("Startup", "ServiceRegistration.cs");

        Assert.Contains("OnTokenValidated", registration);
        Assert.Contains("user.IsLocked", registration);
        Assert.Contains("context.Fail", registration);
        Assert.Contains("AsNoTracking()", registration);
    }

    [Fact]
    public void SensitivePublicAndUserActionsAreRateLimited()
    {
        var registration = Source("Startup", "ServiceRegistration.cs");
        var pipeline = Source("Startup", "ApplicationPipeline.cs");
        var credentials = Source("Controllers", "Auth", "AuthController.Credentials.cs");
        var passwordReset = Source("Controllers", "Auth", "AuthController.PasswordReset.cs");
        var groupMessages = Source("Controllers", "Community", "CommunityController.GroupMessages.cs");
        var directMessages = Source("Controllers", "Community", "CommunityController.Direct.cs");
        var upload = Source("Controllers", "Venues", "UploadController.cs");

        Assert.Contains("services.AddPicklinkRateLimits()", registration);
        Assert.Contains("app.UseRateLimiter()", pipeline);
        Assert.Contains("RateLimitPolicies.Authentication", credentials);
        Assert.Contains("RateLimitPolicies.Authentication", passwordReset);
        Assert.Contains("RateLimitPolicies.Messaging", groupMessages);
        Assert.Contains("RateLimitPolicies.Messaging", directMessages);
        Assert.Contains("RateLimitPolicies.Upload", upload);
    }

    [Fact]
    public void SelfServiceRoleAssignmentDoesNotAllowStaff()
    {
        var service = Source("Services", "Auth", "AuthService.cs");
        var request = Source("DTOs", "AssignRoleRequest.cs");

        Assert.DoesNotContain("case \"Staff\"", service);
        Assert.DoesNotContain("Player, VenueOwner, Staff", service);
        Assert.DoesNotContain("Player\", \"VenueOwner\", \"Staff", request);
    }

    [Fact]
    public void CredentialRecoveryDoesNotRevealWhetherEmailExists()
    {
        var service = Source("Services", "Auth", "AuthService.cs");

        Assert.Contains("user is null || !_passwordHasher.Verify", service);
        Assert.Contains("CreateForgotPasswordResponse(expiresAt)", service);
        Assert.Contains("return AuthServiceResult<ForgotPasswordResponse>.Success(response);", service);
        Assert.DoesNotContain("Email nay chua duoc dang ky", service);
    }

    private static string Source(params string[] relativeSegments) =>
        File.ReadAllText(SourcePath(relativeSegments));

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
