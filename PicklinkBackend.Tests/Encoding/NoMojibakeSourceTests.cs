using System.Text.RegularExpressions;

namespace PicklinkBackend.Tests.TextEncoding;

public sealed class NoMojibakeSourceTests
{
    private static readonly Regex MojibakePattern = new(
        "Ã|Â|Ä|Æ|â€|áº|á»",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void BackendSourceDoesNotContainMojibake()
    {
        var sourceRoot = SourceDirectory();
        var offenders = Directory
            .EnumerateFiles(sourceRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is ".cs" or ".json")
            .Where(path => !IsIgnored(sourceRoot, path))
            .Where(path => MojibakePattern.IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(sourceRoot, path).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(path => path)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"Mojibake found in backend source:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}");
    }

    private static bool IsIgnored(string sourceRoot, string path)
    {
        var segments = Path.GetRelativePath(sourceRoot, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment => segment is "bin" or "obj" or "Migrations");
    }

    private static string SourceDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "PicklinkBackend");
            if (File.Exists(Path.Combine(candidate, "PicklinkBackend.csproj")))
                return candidate;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the backend source directory.");
    }
}
