namespace PicklinkBackend.Tests.ApiContracts;

public class StaffCheckInGroupContractTests
{
    [Fact]
    public void StaffCanSearchAndMapAnIndividualCheckInGroupCode()
    {
        var service = File.ReadAllText(SourcePath("Services", "Staff", "StaffOperationService.cs"));
        var dto = File.ReadAllText(SourcePath("DTOs", "StaffOperationsDtos.cs"));

        Assert.Contains("item.CheckInGroups.Any(group => group.CheckInCode.ToUpper() == normalized)", service);
        Assert.Contains("public string CheckInCode { get; set; }", dto);
        Assert.Contains("CheckInCode = group.CheckInCode", service);
    }

    private static string SourcePath(params string[] relativeSegments)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "PicklinkBackend"));
        return Path.Combine(new[] { root }.Concat(relativeSegments).ToArray());
    }
}
