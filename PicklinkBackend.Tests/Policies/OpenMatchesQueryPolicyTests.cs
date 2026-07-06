namespace PicklinkBackend.Tests;

public class OpenMatchesQueryPolicyTests
{
    [Fact]
    public void OpenMatchesUsesALeanSearchQueryInsteadOfDetailIncludes()
    {
        var source = File.ReadAllText(MatchControllerSourcePath());

        Assert.Contains("var query = MatchSearchQuery(asNoTracking: true)", source);

        var searchQuery = ExtractMethod(source, "private IQueryable<Match> MatchSearchQuery");
        Assert.DoesNotContain("Conversations", searchQuery);
        Assert.DoesNotContain("MatchCheckIns", searchQuery);
        Assert.DoesNotContain("StatusHistories", searchQuery);
        Assert.DoesNotContain("BookingRules", searchQuery);
    }

    [Fact]
    public void MyMatchesAvoidsCollectionsThatAreNotRenderedByTheList()
    {
        var source = File.ReadAllText(MatchControllerSourcePath());

        Assert.Contains("var query = MyMatchesQuery(asNoTracking: true)", source);

        var myMatchesQuery = ExtractMethod(source, "private IQueryable<Match> MyMatchesQuery");
        Assert.Contains("MatchParticipants", myMatchesQuery);
        Assert.Contains("Bookings", myMatchesQuery);
        Assert.DoesNotContain("AvailabilitySlots", myMatchesQuery);
        Assert.DoesNotContain("Payments", myMatchesQuery);
        Assert.DoesNotContain("HostPlayer", myMatchesQuery);
    }

    [Fact]
    public void OpenMatchesAppliesOwnerFilteringBeforePagination()
    {
        var source = File.ReadAllText(MatchControllerSourcePath());
        var endpoint = ExtractMethod(source, "public async Task<ActionResult<PaginatedResponse<MatchSearchResponse>>> GetOpenMatches");

        Assert.Contains("normalizedOwner == \"mine\"", endpoint);
        Assert.Contains("normalizedOwner == \"other\"", endpoint);
        Assert.True(
            endpoint.IndexOf("normalizedOwner == \"mine\"", StringComparison.Ordinal)
            < endpoint.IndexOf("var totalCount", StringComparison.Ordinal));
    }

    private static string MatchControllerSourcePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "PicklinkBackend",
                "Controllers",
                "Matches",
                "MatchPhase8Controller.cs");
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate MatchPhase8Controller.cs from the test output directory.");
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find method signature: {signature}");

        var nextMethod = source.IndexOf("\n    private ", start + signature.Length, StringComparison.Ordinal);
        return nextMethod < 0 ? source[start..] : source[start..nextMethod];
    }
}
