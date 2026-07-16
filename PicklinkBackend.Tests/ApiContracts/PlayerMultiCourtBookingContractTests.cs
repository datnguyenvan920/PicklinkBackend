namespace PicklinkBackend.Tests;

public class PlayerMultiCourtBookingContractTests
{
    [Fact]
    public void PlayerHoldContractAcceptsCourtPerSlotAndReturnsOneBooking()
    {
        var source = File.ReadAllText(SourcePath("DTOs", "PlayerBookingDtos.cs"));

        Assert.Contains("public class CreateBookingHoldSlotRequest", source);
        Assert.Contains("public TimeOnly StartTime { get; set; }", source);
        Assert.Contains("public List<CreateBookingHoldSlotRequest> Slots { get; set; } = [];", source);
        Assert.Contains("public List<BookingSlotResponse> Slots { get; set; } = [];", source);
        Assert.Contains("public List<BookingCheckInGroupResponse> CheckInGroups { get; set; } = [];", source);
    }

    [Fact]
    public void PlayerHoldContractRequiresNewSlotsInsteadOfLegacySlotStarts()
    {
        var source = File.ReadAllText(SourcePath("DTOs", "PlayerBookingDtos.cs"));

        Assert.DoesNotContain("public List<TimeOnly> SlotStarts", source);
        Assert.Contains("[Required, MinLength(1), MaxLength(16)]\n    public List<CreateBookingHoldSlotRequest> Slots", source);
    }

    [Fact]
    public void CreateHoldingCreatesOneBookingWithSlotsAndCheckInGroups()
    {
        var method = ExtractMethod(PlayerBookingServiceSource(), "CreateHolding", "GetMyBookings");

        Assert.Contains("ServiceResult<BookingHoldingResponse>> CreateHolding", File.ReadAllText(PlayerBookingServiceSource()));
        Assert.Contains("request.Slots", method);
        Assert.Contains("booking.Slots.Add", method);
        Assert.Contains("booking.CheckInGroups.Add", method);
        Assert.DoesNotContain("var createdBookings = new List<Booking>()", method);
    }

    [Fact]
    public void CreateHoldingRejectsOverlappingSelectedSlots()
    {
        var method = ExtractMethod(PlayerBookingServiceSource(), "CreateHolding", "GetMyBookings");

        Assert.Contains("selectedRanges.Where((slot, index) =>", method);
        Assert.Contains("slot.Start < other.End && slot.End > other.Start", method);
    }

    [Fact]
    public void CreateHoldingChecksExistingChildSlotsInsteadOfTheBookingRange()
    {
        var method = ExtractMethod(PlayerBookingServiceSource(), "CreateHolding", "GetMyBookings");

        Assert.Contains(".Include(booking => booking.Slots)", method);
        Assert.Contains("booking.Slots.Any(existingSlot => existingSlot.CourtId == slot.CourtId", method);
        Assert.Contains("!booking.Slots.Any()", method);
    }

    [Fact]
    public void MyBookingsUsesSplitQueryForNestedCollections()
    {
        var source = File.ReadAllText(PlayerBookingServiceSource());
        var start = source.IndexOf(
            "public async Task<ServiceResult<PaginatedResponse<BookingHoldingResponse>>> GetMyBookings",
            StringComparison.Ordinal);
        var end = source.IndexOf(
            "public async Task<ServiceResult<BookingHoldingResponse>> GetBooking",
            start,
            StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start);
        Assert.Contains(".AsSplitQuery()", source[start..end]);
    }

    [Fact]
    public void DatabaseQueriesSplitCollectionsByDefault()
    {
        var source = File.ReadAllText(SourcePath("Startup", "ServiceRegistration.cs"));

        Assert.Contains("UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)", source);
    }

    [Fact]
    public void PlayerCanLoadAllBookingsInOnePaymentGroup()
    {
        var source = File.ReadAllText(PlayerBookingServiceSource());

        Assert.Contains("GetHoldingGroup", source);
        Assert.Contains("item.PaymentGroupId == paymentGroupId", source);
        Assert.Contains("BookingHoldingGroupResponse", source);
    }

    private static string PlayerBookingServiceSource() => SourcePath("Services", "Bookings", "PlayerBookingService.cs");

    private static string ExtractMethod(string path, string methodName, string nextMethodName)
    {
        var source = File.ReadAllText(path);
        var start = source.IndexOf($"public async Task<ServiceResult<BookingHoldingResponse>> {methodName}", StringComparison.Ordinal);
        var end = source.IndexOf($"public async Task<ServiceResult<PaginatedResponse<BookingHoldingResponse>>> {nextMethodName}", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Could not locate {methodName}.");
        return source[start..end];
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
