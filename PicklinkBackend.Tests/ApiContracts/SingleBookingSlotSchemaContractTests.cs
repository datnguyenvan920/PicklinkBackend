namespace PicklinkBackend.Tests;

public class SingleBookingSlotSchemaContractTests
{
    [Fact]
    public void BookingSchemaDefinesSlotsAndCheckInGroups()
    {
        var booking = File.ReadAllText(SourcePath("Models", "Booking.cs"));
        var dbContext = File.ReadAllText(SourcePath("Data", "ApplicationDbContext.cs"));
        var schemaStartup = File.ReadAllText(SourcePath("Startup", "SchemaStartup.cs"));

        Assert.Contains("public virtual ICollection<BookingSlot> Slots", booking);
        Assert.Contains("public virtual ICollection<BookingCheckInGroup> CheckInGroups", booking);
        Assert.Contains("entity.ToTable(\"BOOKING_SLOT\")", dbContext);
        Assert.Contains("entity.ToTable(\"BOOKING_CHECKIN_GROUP\")", dbContext);
        Assert.Contains("UQ_BOOKING_CHECKIN_GROUP_code", dbContext);
        Assert.Contains("EnsureBookingSlotSchema(app)", schemaStartup);
        Assert.Contains("CREATE TABLE [BOOKING_SLOT]", schemaStartup);
        Assert.Contains("CREATE TABLE [BOOKING_CHECKIN_GROUP]", schemaStartup);
        Assert.DoesNotContain("ON DELETE SET NULL", schemaStartup);
        Assert.DoesNotContain("DeleteBehavior.SetNull", dbContext);
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
