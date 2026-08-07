namespace PicklinkBackend.Tests.Security;

public sealed class AuthHardeningContractTests
{
    [Fact]
    public void ExistingJwtIsRejectedWhenAccountIsLockedOrMissing()
    {
        var registration = Source("Startup", "ServiceRegistration.cs");
        var accountStatus = Source("Services", "Auth", "AccountStatusCache.cs");

        // Token validation still refuses the request itself...
        Assert.Contains("OnTokenValidated", registration);
        Assert.Contains("IsUsableAsync", registration);
        Assert.Contains("context.Fail", registration);

        // ...while the lock lookup it depends on lives in the cache.
        Assert.Contains("user.IsLocked", accountStatus);
        Assert.Contains("AsNoTracking()", accountStatus);
    }

    [Fact]
    public void LockingAnAccountTakesEffectWithoutWaitingForTheCacheToExpire()
    {
        var accountStatus = Source("Services", "Auth", "AccountStatusCache.cs");
        var adminUsers = Source("Services", "Admin", "Implementations", "AdminUserService.cs");

        Assert.Contains("public void Invalidate(int userId)", accountStatus);
        // Both LockAsync and UnlockAsync must drop the cached decision.
        Assert.Equal(2, Occurrences(adminUsers, "_accountStatus.Invalidate(userId);"));
    }

    private static int Occurrences(string source, string value)
    {
        var count = 0;
        for (var index = source.IndexOf(value, StringComparison.Ordinal); index >= 0;
             index = source.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
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
