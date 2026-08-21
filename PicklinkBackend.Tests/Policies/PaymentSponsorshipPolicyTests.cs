namespace PicklinkBackend.Tests.Policies;

public class PaymentSponsorshipPolicyTests
{
    [Fact]
    public void ProxyPaymentRequiresAcceptedRequestAndAnExclusiveClaim()
    {
        var source = File.ReadAllText(SourcePath("Services", "Payments", "Implementations", "PaymentService.cs"));
        var previewStart = source.IndexOf(" PreviewBatchTransfer(", StringComparison.Ordinal);
        var submitStart = source.IndexOf(" SubmitBatchTransfer(", previewStart, StringComparison.Ordinal);
        var submitEnd = source.IndexOf(" SubmitTransfer(", submitStart, StringComparison.Ordinal);
        var preview = source[previewStart..submitStart];
        var submit = source[submitStart..submitEnd];

        Assert.Contains("!IsAcceptedSponsorship(item) || item.ClaimedByPlayerId != currentPlayer.PlayerId", preview);
        Assert.Contains("payment.ClaimedByPlayerId = currentPlayer.PlayerId", preview);
        Assert.Contains("payment.ClaimExpiresAt = claimExpiresAt", preview);
        Assert.Contains("item.Status != \"Pending\"", submit);
        Assert.Contains("!HasActivePaymentClaim(item, now)", submit);
        Assert.Contains("item.ClaimedByPlayerId != currentPlayer.PlayerId", submit);
    }

    [Fact]
    public void ShareOwnerMustAcceptARequestBeforeAnotherPlayerCanPay()
    {
        var payment = File.ReadAllText(SourcePath("Models", "Payment.cs"));
        var controller = File.ReadAllText(SourcePath("Controllers", "Payments", "PaymentController.cs"));
        var service = File.ReadAllText(SourcePath("Services", "Payments", "Implementations", "PaymentService.cs"));

        Assert.Contains("public bool AllowPaymentByOthers", payment);
        Assert.Contains("public int? ClaimedByPlayerId", payment);
        Assert.Contains("public DateTime? ClaimExpiresAt", payment);
        Assert.Contains("bookings/{bookingId:int}/sponsorship-requests/{targetPlayerId:int}", controller);
        Assert.Contains("bookings/{bookingId:int}/sponsorship-requests/respond", controller);
        Assert.Contains("Có yêu cầu trả hộ thanh toán", service);
        Assert.Contains("payment.AllowPaymentByOthers = true", service);
        Assert.Contains("payment.ClaimedByPlayerId!.Value", service);
        Assert.Contains("Bạn đã đồng ý để thành viên khác trả phần thanh toán này.", service);
        Assert.Contains("Yêu cầu trả hộ đã được đồng ý", service);
    }

    [Fact]
    public void PendingSponsorshipCannotBeOverwrittenByQrPreviewOrSubmission()
    {
        var service = File.ReadAllText(SourcePath("Services", "Payments", "Implementations", "PaymentService.cs"));

        Assert.True(service.Split("payments.Any(IsPendingSponsorshipRequest)", StringSplitOptions.None).Length >= 3);
    }

    [Fact]
    public void AcceptedSponsorshipMustBeIncludedInPreviewAndSubmission()
    {
        var service = File.ReadAllText(SourcePath("Services", "Payments", "Implementations", "PaymentService.cs"));
        var previewStart = service.IndexOf(" PreviewBatchTransfer(", StringComparison.Ordinal);
        var cancelStart = service.IndexOf(" CancelPaymentSponsorship(", previewStart, StringComparison.Ordinal);
        var submitStart = service.IndexOf(" SubmitBatchTransfer(", cancelStart, StringComparison.Ordinal);
        var submitEnd = service.IndexOf(" SubmitTransfer(", submitStart, StringComparison.Ordinal);

        Assert.Contains("OmitsAcceptedSponsorship(booking, currentPlayer.PlayerId, targetParticipantIds)", service[previewStart..cancelStart]);
        Assert.Contains("OmitsAcceptedSponsorship(booking, currentPlayer.PlayerId, targetParticipantIds)", service[submitStart..submitEnd]);
        Assert.Contains("Các phần đã đồng ý cho bạn trả hộ phải được thanh toán cùng nhau.", service);
    }

    [Fact]
    public void SponsorCanCancelAnAcceptedRequestAndReleaseTheTarget()
    {
        var controller = File.ReadAllText(SourcePath("Controllers", "Payments", "PaymentController.cs"));
        var service = File.ReadAllText(SourcePath("Services", "Payments", "Implementations", "PaymentService.cs"));

        Assert.Contains("[HttpDelete(\"bookings/{bookingId:int}/sponsorship-requests/{targetPlayerId:int}\")]", controller);
        Assert.Contains("public async Task<ServiceResult<PaymentSponsorshipResponse>> CancelPaymentSponsorship", service);
        Assert.Contains("payment.AllowPaymentByOthers = false", service);
        Assert.Contains("ClearPaymentClaim(payment)", service);
        Assert.Contains("Yêu cầu trả hộ đã được hủy", service);
        Assert.Contains("PaymentSponsorshipCancelled", service);
    }

    private static string SourcePath(params string[] segments)
    {
        var fileName = segments.Last();
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var projectDir = Path.Combine(directory.FullName, "PicklinkBackend");
            if (Directory.Exists(projectDir))
            {
                var candidate = Path.Combine([projectDir, .. segments]);
                if (File.Exists(candidate)) return candidate;

                var found = Directory.GetFiles(projectDir, fileName, SearchOption.AllDirectories).FirstOrDefault();
                if (found is not null) return found;
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {string.Join('/', segments)}.");
    }
}
