using System.Text.RegularExpressions;

namespace PicklinkBackend.Tests;

public class PersistedTextEncodingContractTests
{
    private static readonly string[] MojibakeMarkers =
        ["Ã", "Ä", "á»", "áº", "Æ", "ƒ", "â€", "â„"];

    private static readonly string[] PersistedProducerPatterns =
    [
        @"new\s+NotificationInput\s*\((.*?)\)\s*\)",
        @"new\s+NotificationLog\s*\{(.*?)\};",
        @"new\s+BookingStatusHistory\s*\{(.*?)\}",
        @"new\s+PaymentStatusHistory\s*\{(.*?)\}",
        @"(?:NewHistory|NewPaymentHistory|NewMatchBookingHistory|NewMatchPaymentHistory|Expire)\s*\((.*?)\)\s*;",
        @"(?:QueueNotification|NotifyGroupManagersAsync|NotifyGroupMembersAsync)\s*\((.*?)\)\s*;",
        @"new\s+Booking\s*\{(.*?)\};",
        @"public\s+string\??\s+(?:Title|Message|LinkLabel)\s*\{[^\r\n]*",
        @"DF_NOTIFICATION_LOG_title[^\r\n]*"
    ];

    [Fact]
    public void DatabaseTextProducersDoNotContainMojibake()
    {
        var backendRoot = BackendRoot();
        var files = Directory.EnumerateFiles(Path.Combine(backendRoot, "PicklinkBackend", "Services"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(backendRoot, "PicklinkBackend.MatchmakingWorker"), "*.cs"))
            .Append(Path.Combine(backendRoot, "PicklinkBackend", "Startup", "SchemaStartup.cs"))
            .Append(Path.Combine(backendRoot, "PicklinkBackend", "Models", "NotificationLog.cs"))
            .Append(Path.Combine(backendRoot, "PicklinkBackend", "Models", "Booking.cs"));

        var offenders = files.SelectMany(PersistedSegments)
            .Where(item => MojibakeMarkers.Any(marker => item.Text.Contains(marker, StringComparison.Ordinal)))
            .Select(item => $"{Path.GetFileName(item.Path)}:{item.Line}")
            .Distinct()
            .ToList();

        Assert.True(offenders.Count == 0, $"Mojibake found in persisted text producers: {string.Join(", ", offenders)}");
    }

    private static IEnumerable<(string Path, int Line, string Text)> PersistedSegments(string path)
    {
        var source = File.ReadAllText(path);
        foreach (var pattern in PersistedProducerPatterns)
        foreach (Match match in Regex.Matches(source, pattern, RegexOptions.Singleline))
            yield return (path, source[..match.Index].Count(character => character == '\n') + 1, match.Value);
    }

    private static string BackendRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PicklinkBackend.sln"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate PicklinkBackend.sln.");
    }
}
