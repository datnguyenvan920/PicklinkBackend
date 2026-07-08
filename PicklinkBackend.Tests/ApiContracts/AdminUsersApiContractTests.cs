namespace PicklinkBackend.Tests;

public class AdminUsersApiContractTests
{
    [Fact]
    public void AdminUsersControllerExposesProtectedRealDataEndpoints()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "Admin", "AdminUsersController.cs"));
        var queryService = File.ReadAllText(SourcePath("Services", "Admin", "AdminUserQueryService.cs"));
        var lockService = File.ReadAllText(SourcePath("Services", "Admin", "AdminUserLockService.cs"));
        var dtos = File.ReadAllText(SourcePath("DTOs", "AdminUserDtos.cs"));

        Assert.Contains("[Authorize(Roles = \"Admin\")]", source);
        Assert.Contains("[Route(\"api/admin/users\")]", source);
        Assert.Contains("[HttpGet]", source);
        Assert.Contains("[HttpPost(\"{userId:int}/lock\")]", source);
        Assert.Contains("[HttpPost(\"{userId:int}/unlock\")]", source);
        Assert.Contains("AdminUserQueryService", source);
        Assert.Contains("AdminUserLockService", source);
        Assert.DoesNotContain("ApplicationDbContext", source);
        Assert.DoesNotContain("public class AdminUserSummaryResponse", source);
        Assert.Contains("Pagination.NormalizePage", queryService);
        Assert.Contains("Pagination.NormalizePageSize", queryService);
        Assert.Contains("Pagination.Create", queryService);
        Assert.Contains("user.Username.Contains(keyword)", queryService);
        Assert.Contains("user.Email.Contains(keyword)", queryService);
        Assert.Contains("user.UserType == normalizedRole", queryService);
        Assert.Contains("user.IsLocked", queryService);
        Assert.Contains("IsLocked = true", lockService);
        Assert.Contains("IsLocked = false", lockService);
        Assert.Contains("AdminUserLockRequest", dtos);
        Assert.Contains("AdminUserSummaryResponse", dtos);
        Assert.DoesNotContain("Tournament", source);
    }

    [Fact]
    public void UserLockStateIsPersistedAndBlocksLogin()
    {
        var user = File.ReadAllText(SourcePath("Models", "User.cs"));
        var dbContext = File.ReadAllText(SourcePath("Data", "ApplicationDbContext.cs"));
        var schemaStartup = File.ReadAllText(SourcePath("Startup", "SchemaStartup.cs"));
        var auth = File.ReadAllText(SourcePath("Services", "Auth", "AuthService.cs"));

        Assert.Contains("public bool IsLocked { get; set; }", user);
        Assert.Contains(".HasColumnName(\"isLocked\")", dbContext);
        Assert.Contains("COL_LENGTH(N'USER', N'isLocked')", schemaStartup);
        Assert.Contains("if (user.IsLocked)", auth);
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
