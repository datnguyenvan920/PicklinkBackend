namespace PicklinkBackend.Tests;

public class AdminUsersApiContractTests
{
    [Fact]
    public void AdminUsersControllerExposesProtectedRealDataEndpoints()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "Admin", "AdminUsersController.cs"));
        var userService = File.ReadAllText(SourcePath("Services", "Admin", "Implementations", "AdminUserService.cs"));
        var dtos = File.ReadAllText(SourcePath("DTOs", "AdminUserDtos.cs"));

        Assert.Contains("[Authorize(Roles = \"Admin\")]", source);
        Assert.Contains("[Route(\"api/admin/users\")]", source);
        Assert.Contains("[HttpGet]", source);
        Assert.Contains("[HttpPost(\"owners\")]", source);
        Assert.Contains("CreateVenueOwnerAsync", source);
        Assert.Contains("[HttpPost(\"{userId:int}/lock\")]", source);
        Assert.Contains("[HttpPost(\"{userId:int}/unlock\")]", source);
        Assert.Contains("IAdminUserService", source);
        Assert.DoesNotContain("ApplicationDbContext", source);
        Assert.Contains("Pagination.NormalizePage", userService);
        Assert.Contains("Pagination.NormalizePageSize", userService);
        Assert.Contains("Pagination.Create", userService);
        Assert.Contains("_adminRepository.GetAdminUserListAsync", userService);
        Assert.Contains("IsLocked = true", userService);
        Assert.Contains("IsLocked = false", userService);
        Assert.Contains("AdminUserLockRequest", dtos);
        Assert.Contains("AdminCreateVenueOwnerRequest", dtos);
        Assert.Contains("PhoneNumber", dtos);
        Assert.Contains("PhoneNumber = request.PhoneNumber.Trim()", userService);
        Assert.Contains("UserType = \"VenueOwner\"", userService);
        Assert.Contains("_passwordHasher.Hash(request.Password)", userService);
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
