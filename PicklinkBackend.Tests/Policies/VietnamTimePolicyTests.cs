using PicklinkBackend.Services.Shared;

namespace PicklinkBackend.Tests;

public class VietnamTimePolicyTests
{
    [Fact]
    public void SchedulingUsesVietnamWallClockAndNeverServerLocalTime()
    {
        var utc = new DateTime(2026, 7, 20, 18, 30, 0, DateTimeKind.Utc);
        var local = VietnamTime.FromUtc(utc);

        Assert.Equal(new DateTime(2026, 7, 21, 1, 30, 0, DateTimeKind.Unspecified), local);
        Assert.Equal(utc, VietnamTime.ToUtc(local));

        var root = SourceRoot();
        var source = string.Join('\n', Directory.EnumerateFiles(
            Path.Combine(root, "Services"), "*.cs", SearchOption.AllDirectories)
            .Append(Path.Combine(root, "DTOs", "MatchmakingDto.cs"))
            .Append(Path.Combine(root, "..", "PicklinkBackend.MatchmakingWorker", "MatchmakingWorker.cs"))
            .Select(File.ReadAllText));
        Assert.DoesNotContain("DateTime.Now", source);
        Assert.DoesNotContain("DateTime.Today", source);
        Assert.DoesNotContain(".ToLocalTime()", source);
        Assert.DoesNotContain("StartTime = AsUtc(", source);
        Assert.DoesNotContain("EndTime = AsUtc(", source);
    }

    private static string SourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "PicklinkBackend");
            if (File.Exists(Path.Combine(candidate, "PicklinkBackend.csproj"))) return candidate;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate PicklinkBackend source root.");
    }
}
