namespace PicklinkBackend.Tests;

public class AdminUsersApiContractTests
{
    [Fact]
    public void AdminUsersControllerExposesProtectedRealDataEndpoints()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "AdminUsersController.cs"));

        Assert.Contains("[Authorize(Roles = \"Admin\")]", source);
        Assert.Contains("[Route(\"api/admin/users\")]", source);
        Assert.Contains("[HttpGet]", source);
        Assert.Contains("[HttpPost(\"{userId:int}/lock\")]", source);
        Assert.Contains("[HttpPost(\"{userId:int}/unlock\")]", source);
        Assert.Contains("Pagination.NormalizePage", source);
        Assert.Contains("Pagination.NormalizePageSize", source);
        Assert.Contains("Pagination.Create", source);
        Assert.Contains("user.Username.Contains(keyword)", source);
        Assert.Contains("user.Email.Contains(keyword)", source);
        Assert.Contains("user.UserType == normalizedRole", source);
        Assert.Contains("user.IsLocked", source);
        Assert.DoesNotContain("Tournament", source);
    }

    [Fact]
    public void UserLockStateIsPersistedAndBlocksLogin()
    {
        var user = File.ReadAllText(SourcePath("Models", "User.cs"));
        var dbContext = File.ReadAllText(SourcePath("Data", "ApplicationDbContext.cs"));
        var program = File.ReadAllText(SourcePath("Program.cs"));
        var auth = File.ReadAllText(SourcePath("Controllers", "AuthController.cs"));

        Assert.Contains("public bool IsLocked { get; set; }", user);
        Assert.Contains(".HasColumnName(\"isLocked\")", dbContext);
        Assert.Contains("COL_LENGTH(N'USER', N'isLocked')", program);
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
