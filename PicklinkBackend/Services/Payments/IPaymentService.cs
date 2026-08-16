using PicklinkBackend.DTOs;
using PicklinkBackend.Services.Shared;

namespace PicklinkBackend.Services.Payments;

public interface IPaymentService
{
    void SetCurrentUserId(int? userId);
    Task<ServiceResult<OwnerBankAccountResponse>> GetBankAccount(CancellationToken cancellationToken);
    Task<ServiceResult<OwnerBankAccountResponse>> UpsertBankAccount(OwnerBankAccountRequest request, CancellationToken cancellationToken);
    Task<ServiceResult<BatchPaymentPreviewResponse>> PreviewBatchTransfer(int bookingId, BatchPaymentPreviewRequest request, CancellationToken cancellationToken);
    Task<ServiceResult<PaymentSponsorshipResponse>> RequestPaymentSponsorship(int bookingId, int targetPlayerId, CancellationToken cancellationToken);
    Task<ServiceResult<PaymentSponsorshipResponse>> RespondPaymentSponsorship(int bookingId, RespondPaymentSponsorshipRequest request, CancellationToken cancellationToken);
    Task<ServiceResult<BatchPaymentResponse>> SubmitBatchTransfer(int bookingId, SubmitBatchPaymentReceiptRequest request, CancellationToken cancellationToken);
    Task<ServiceResult<BankTransferResponse>> SubmitTransfer(int bookingId, SubmitPaymentReceiptRequest request, CancellationToken cancellationToken);
    Task<ServiceResult<BankTransferResponse>> SubmitTicketTransfer(int sessionTicketId, SubmitPaymentReceiptRequest request, CancellationToken cancellationToken);
    Task<ServiceResult<BatchPaymentResponse>> SubmitPlayerBookingGroupTransfer(Guid paymentGroupId, SubmitPaymentReceiptRequest request, CancellationToken cancellationToken);
    Task<ServiceResult<PaginatedResponse<BankTransferResponse>>> GetOperatorPayments(string status = "WaitingForConfirmation", int page = 1, int pageSize = Pagination.DefaultPageSize, CancellationToken cancellationToken = default);
    Task<ServiceResult<CheckoutBookingContextResponse>> GetCheckoutBookingContext(int bookingId, CancellationToken cancellationToken);
    Task<ServiceResult<BankTransferResponse>> GetPlayerBookingPayment(int bookingId, CancellationToken cancellationToken);
    Task<ServiceResult<BankTransferResponse>> GetOperatorPayment(int paymentId, CancellationToken cancellationToken);
    Task<ServiceResult<List<BankTransferResponse>>> GetOperatorBookingPayments(int bookingId, CancellationToken cancellationToken);
    Task<ServiceResult<BankTransferResponse>> ApprovePayment(int paymentId, CancellationToken cancellationToken);
    Task<ServiceResult<BankTransferResponse>> RejectPayment(int paymentId, RejectPaymentRequest request, CancellationToken cancellationToken);
}
