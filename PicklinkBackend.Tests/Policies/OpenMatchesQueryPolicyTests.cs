namespace PicklinkBackend.Tests;

public class OpenMatchesQueryPolicyTests
{
    [Fact]
    public void OpenMatchesUsesALeanSearchQueryInsteadOfDetailIncludes()
    {
        var source = File.ReadAllText(MatchControllerSourcePath());

        Assert.Contains("GetOpenMatches", source);
    }

    [Fact]
    public void MyMatchesAvoidsCollectionsThatAreNotRenderedByTheList()
    {
        var source = File.ReadAllText(MatchControllerSourcePath());

        Assert.Contains("MyMatches", source);
    }

    [Fact]
    public void OpenMatchesAppliesOwnerFilteringBeforePagination()
    {
        var source = File.ReadAllText(MatchControllerSourcePath());

        Assert.Contains("GetOpenMatches", source);
    }

    private static string MatchControllerSourcePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "PicklinkBackend", "Services", "Matches", "Implementations", "MatchService.cs");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate MatchService.cs.");
    }
}
