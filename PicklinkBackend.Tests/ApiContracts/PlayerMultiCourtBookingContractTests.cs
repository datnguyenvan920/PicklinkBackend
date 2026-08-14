namespace PicklinkBackend.Tests;

public class PlayerMultiCourtBookingContractTests
{
    [Fact]
    public void PlayerHoldContractAcceptsCourtPerSlotAndReturnsOneBooking()
    {
        var source = File.ReadAllText(SourcePath("DTOs", "PlayerBookingDtos.cs"));

        Assert.Contains("public class CreateBookingHoldSlotRequest", source);
        Assert.Contains("public DateOnly? Date { get; set; }", source);
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
        Assert.Contains("public List<CreateBookingHoldSlotRequest> Slots", source);
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

        Assert.Contains("possiblyOverlappingBookings", method);
        Assert.Contains("bookingObj.Slots.Any", method);
    }

    [Fact]
    public void CreateHoldingSerializesPlayerAndCourtSchedulesBeforeCheckingAvailability()
    {
        var method = ExtractMethod(PlayerBookingServiceSource(), "CreateHolding", "GetMyBookings");

        Assert.Contains("player-schedule:{player.PlayerId}", method);
        Assert.Contains("court-schedule:{slot.CourtId}:{slot.Start:yyyyMMdd}", method);
        Assert.Contains("OrderBy(resource => resource, StringComparer.Ordinal)", method);
        Assert.Contains("SqlServerBookingLock.AcquireAsync(transaction, resource", method);
        Assert.True(method.IndexOf("courtScheduleLocks", StringComparison.Ordinal)
            < method.IndexOf("GetPotentiallyOverlappingBookingsAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void MyBookingsUsesSplitQueryForNestedCollections()
    {
        var repoSource = File.ReadAllText(SourcePath("Repositories", "BookingRepository.cs"));

        Assert.Contains(".AsSplitQuery()", repoSource);
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
