namespace PicklinkBackend.Tests;

public class CommunityControllerStructureTests
{
    [Fact]
    public void CommunityControllerIsSplitByResponsibility()
    {
        var controllerDirectory = SourceDirectory("Controllers", "Community");
        var expectedFiles = new[]
        {
            "CommunityController.cs",
            "CommunityController.Groups.cs",
            "CommunityController.Members.cs",
            "CommunityController.GroupImages.cs",
            "CommunityController.Posts.cs",
            "CommunityController.Comments.cs",
            "CommunityController.Responses.cs",
            "CommunityController.Helpers.cs",
            "CommunityController.Direct.cs",
            "CommunityController.GroupMessages.cs"
        };

        foreach (var fileName in expectedFiles)
        {
            Assert.True(
                File.Exists(Path.Combine(controllerDirectory, fileName)),
                $"Missing expected Community partial file: {fileName}");
        }

        var rootControllerLines = File.ReadAllLines(Path.Combine(controllerDirectory, "CommunityController.cs")).Length;
        Assert.True(rootControllerLines <= 80, "CommunityController.cs should only contain route metadata, constants, and constructor wiring.");
    }

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
