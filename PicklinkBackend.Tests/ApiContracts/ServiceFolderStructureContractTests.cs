namespace PicklinkBackend.Tests.ApiContracts;

public class ServiceFolderStructureContractTests
{
    [Fact]
    public void LargeServiceModulesUseFeatureFolders()
    {
        var servicesDirectory = SourceDirectory("Services");

        AssertServiceExists(servicesDirectory, "Admin", "AdminUserQueryService.cs");
        AssertServiceExists(servicesDirectory, "Admin", "AdminVenueQueryService.cs");
        AssertServiceExists(servicesDirectory, "Auth", "AuthService.cs");
        AssertServiceExists(servicesDirectory, "Auth", "JwtTokenService.cs");
        AssertServiceExists(servicesDirectory, "Bookings", "PlayerBookingService.cs");
        AssertServiceExists(servicesDirectory, "Bookings", "BookingHoldExpirationService.cs");
        AssertServiceExists(servicesDirectory, "Community", "CommunityService.cs");
        AssertServiceExists(servicesDirectory, "Community", "CommunityDiscoveryService.cs");
        AssertServiceExists(servicesDirectory, "Infrastructure", "SmtpEmailSender.cs");
        AssertServiceExists(servicesDirectory, "ListingFees", "ListingFeeReminderService.cs");
        AssertServiceExists(servicesDirectory, "Matches", "MatchService.cs");
        AssertServiceExists(servicesDirectory, "Matches", "MatchService.Open.cs");
        AssertServiceExists(servicesDirectory, "Notifications", "NotificationService.cs");
        AssertServiceExists(servicesDirectory, "Owner", "OwnerVenueService.cs");
        AssertServiceExists(servicesDirectory, "Payments", "PaymentService.cs");
        AssertServiceExists(servicesDirectory, "Players", "PlayerProfileService.cs");
        AssertServiceExists(servicesDirectory, "Schedules", "ScheduleRealtimeNotifier.cs");
        AssertServiceExists(servicesDirectory, "Shared", "ServiceResult.cs");
        AssertServiceExists(servicesDirectory, "Staff", "StaffOperationService.cs");
        AssertServiceExists(servicesDirectory, "Venues", "VenueNearbyQueryService.cs");
    }


    [Fact]
    public void ServiceNamespacesFollowFeatureFolders()
    {
        var servicesDirectory = SourceDirectory("Services");
        var serviceFiles = Directory.GetFiles(servicesDirectory, "*.cs", SearchOption.AllDirectories);

        Assert.NotEmpty(serviceFiles);
        foreach (var file in serviceFiles)
        {
            var relativeDirectory = Path.GetRelativePath(servicesDirectory, Path.GetDirectoryName(file)!);
            Assert.DoesNotContain(Path.DirectorySeparatorChar, relativeDirectory);

            var source = File.ReadAllText(file);
            Assert.Contains($"namespace PicklinkBackend.Services.{relativeDirectory};", source);
        }
    }

    [Fact]
    public void CodeDoesNotUseObsoleteRootServiceNamespace()
    {
        var backendRoot = SourceDirectory();
        var sourceFiles = Directory.GetFiles(backendRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

        foreach (var file in sourceFiles)
        {
            Assert.DoesNotContain("using PicklinkBackend.Services;", File.ReadAllText(file));
        }
    }
    [Fact]
    public void ServiceModulesUseExplicitImportsInsteadOfGlobalUsings()
    {
        var backendRoot = SourceDirectory();
        var solutionRoot = Directory.GetParent(backendRoot)!.FullName;
        var sourceRoots = new[]
        {
            backendRoot,
            Path.Combine(solutionRoot, "PicklinkBackend.Tests")
        };

        foreach (var sourceRoot in sourceRoots)
        {
            Assert.False(
                File.Exists(Path.Combine(sourceRoot, "ServiceGlobalUsings.cs")),
                $"{Path.GetFileName(sourceRoot)}/ServiceGlobalUsings.cs should not hide service module dependencies.");

            var sourceFiles = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                    && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

            foreach (var file in sourceFiles)
            {
                Assert.False(System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(file), @"^global\s+using\s+PicklinkBackend\.Services\.", System.Text.RegularExpressions.RegexOptions.Multiline));
            }
        }
    }
    private static void AssertServiceExists(string servicesDirectory, string module, string fileName) =>
        Assert.True(
            File.Exists(Path.Combine(servicesDirectory, module, fileName)),
            $"Services/{module}/{fileName} should exist.");

    private static string SourceDirectory(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                new[] { directory.FullName, "PicklinkBackend" }.Concat(relativeSegments).ToArray());
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not locate {string.Join('/', relativeSegments)}.");
    }
}
