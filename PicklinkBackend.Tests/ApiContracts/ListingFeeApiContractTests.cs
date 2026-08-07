namespace PicklinkBackend.Tests;

public class ListingFeeApiContractTests
{
    [Fact]
    public void ListingFeeModelsAndSchemaAreRegistered()
    {
        var setting = File.ReadAllText(SourcePath("Models", "ListingFeeSetting.cs"));
        var payment = File.ReadAllText(SourcePath("Models", "VenueListingPayment.cs"));
        var dbContext = File.ReadAllText(SourcePath("Data", "ApplicationDbContext.cs"));
        var schemaStartup = File.ReadAllText(SourcePath("Startup", "SchemaStartup.cs"));

        Assert.Contains("public decimal PricePerCourtPerMonth { get; set; }", setting);
        Assert.Contains("public int ActiveCourtCount { get; set; }", payment);
        Assert.Contains("public decimal PricePerCourtPerMonth { get; set; }", payment);
        Assert.Contains("public decimal Amount { get; set; }", payment);
        Assert.Contains("public DateTime? PaidUntil { get; set; }", payment);
        Assert.Contains("DbSet<ListingFeeSetting>", dbContext);
        Assert.Contains("DbSet<VenueListingPayment>", dbContext);
        Assert.Contains("LISTING_FEE_SETTING", dbContext);
        Assert.Contains("VENUE_LISTING_PAYMENT", dbContext);
        Assert.Contains("EnsureListingFeeSchema(app)", schemaStartup);
        Assert.Contains("CREATE TABLE [LISTING_FEE_SETTING]", schemaStartup);
        Assert.Contains("CREATE TABLE [VENUE_LISTING_PAYMENT]", schemaStartup);
    }

    [Fact]
    public void OwnerCanPreviewAndSubmitListingFeeUsingCurrentAdminPrice()
    {
        var controller = File.ReadAllText(SourcePath("Controllers", "Owner", "OwnerVenueController.cs"));
        var source = File.ReadAllText(SourcePath("Services", "Owner", "OwnerVenueService.cs"));

        Assert.Contains("[HttpGet(\"venues/{venueId:int}/listing-fee/preview\")]", controller);
        Assert.Contains("[HttpPost(\"venues/{venueId:int}/listing-fee/payments\")]", controller);
        Assert.Contains("[Consumes(\"multipart/form-data\")]", controller);
        Assert.Contains("GetCurrentListingPriceAsync", source);
        Assert.Contains("ActiveCourtCount", source);
        Assert.Contains("PricePerCourtPerMonth", source);
        Assert.Contains("request.Months", source);
        Assert.Contains("Status = \"PendingReview\"", source);
        Assert.Contains("SaveListingFeeReceiptAsync", source);
    }

    [Fact]
    public void AdminCanConfigurePriceAndReviewListingFeePayments()
    {
        var source = File.ReadAllText(SourcePath("Controllers", "Admin", "AdminListingFeesController.cs"));
        var listingFeeService = File.ReadAllText(SourcePath("Services", "Admin", "Implementations", "AdminListingFeeService.cs"));
        var dtos = File.ReadAllText(SourcePath("DTOs", "AdminListingFeeDtos.cs"));
        var services = File.ReadAllText(SourcePath("Startup", "ServiceRegistration.cs"));

        Assert.Contains("[Authorize(Roles = \"Admin\")]", source);
        Assert.Contains("[Route(\"api/admin/listing-fees\")]", source);
        Assert.Contains("[HttpGet(\"settings\")]", source);
        Assert.Contains("[HttpPut(\"settings\")]", source);
        Assert.Contains("[HttpGet(\"payments\")]", source);
        Assert.Contains("[HttpPost(\"payments/{paymentId:int}/confirm\")]", source);
        Assert.Contains("[HttpPost(\"payments/{paymentId:int}/reject\")]", source);
        Assert.Contains("IAdminListingFeeService", source);
        Assert.Contains("services.AddScoped<IAdminListingFeeService, AdminListingFeeService>()", services);
        Assert.DoesNotContain("ApplicationDbContext", source);
        Assert.Contains("ListingFeeSetting", listingFeeService);
        Assert.Contains("PaidUntil", listingFeeService);
        Assert.Contains("Pagination.Create", listingFeeService);
        Assert.Contains("BeginTransactionAsync", listingFeeService);
        Assert.Contains("admin-listing-payment:{paymentId}", listingFeeService);
        Assert.Contains("admin-listing-venue:{payment.VenueId}", listingFeeService);
        Assert.Contains("transaction.CommitAsync", listingFeeService);
        Assert.Contains("AdminResultStatus.Unauthorized => Unauthorized()", source);
        Assert.Contains("public sealed class ListingFeeSettingsRequest", dtos);
        Assert.Contains("[Required, MinLength(3), MaxLength(500)]", dtos);
        Assert.Contains("public sealed class AdminListingFeePaymentResponse", dtos);
        Assert.DoesNotContain("Tournament", source);
    }

    [Fact]
    public void PublicVenueQueriesOnlyRequireAdminApproval()
    {
        var venue = File.ReadAllText(SourcePath("Services", "Venues", "VenueNearbyQueryService.cs"));
        var playerBooking = File.ReadAllText(SourcePath("Services", "Bookings", "PlayerBookingService.cs"));

        Assert.Contains("venue.ApprovalStatus == \"Approved\"", venue);
        Assert.Contains("_venueRepository.GetApprovedVenuesQueryable()", playerBooking);
        Assert.DoesNotContain("HasActiveListingFee", venue);
        Assert.DoesNotContain("HasActiveListingFee", playerBooking);
    }

    private static string SourcePath(params string[] relativeSegments)
    {
        var cleanSegments = relativeSegments.FirstOrDefault() == "PicklinkBackend" ? relativeSegments[1..] : relativeSegments;
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var projectDir = Path.Combine(directory.FullName, "PicklinkBackend");
            if (Directory.Exists(projectDir))
            {
                var candidate = Path.Combine([projectDir, .. cleanSegments]);
                if (File.Exists(candidate)) return candidate;
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {string.Join('/', relativeSegments)}.");
    }
}
