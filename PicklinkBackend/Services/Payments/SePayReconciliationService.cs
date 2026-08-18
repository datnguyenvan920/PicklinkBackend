using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PicklinkBackend.Repositories;
using PicklinkBackend.Services.Payments.Implementations;
using PicklinkBackend.Services.Security;

namespace PicklinkBackend.Services.Payments;

/// <summary>
/// Backstop for the SePay webhook: lets a screen that's actively polling for payment status
/// (e.g. checkout) also nudge a check against SePay's own transaction list, so confirmation
/// doesn't depend solely on the inbound webhook reaching us in time.
/// </summary>
public sealed class SePayReconciliationService
{
    private static readonly TimeSpan ThrottleWindow = TimeSpan.FromSeconds(4);

    private readonly ISePayTransactionQueryClient _queryClient;
    private readonly SePayWebhookService _webhookService;
    private readonly IPaymentRepository _paymentRepository;
    private readonly IEncryptionService _encryptionService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SePayReconciliationService> _logger;

    public SePayReconciliationService(
        ISePayTransactionQueryClient queryClient,
        SePayWebhookService webhookService,
        IPaymentRepository paymentRepository,
        IEncryptionService encryptionService,
        IMemoryCache cache,
        ILogger<SePayReconciliationService> logger)
    {
        _queryClient = queryClient;
        _webhookService = webhookService;
        _paymentRepository = paymentRepository;
        _encryptionService = encryptionService;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Returns true if a matching SePay transaction was found and applied (caller should
    /// re-read the payment/ticket afterwards). Safe to call frequently: throttled per
    /// transferContent and never throws.
    /// </summary>
    public async Task<bool> TryReconcileAsync(string transferContent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(transferContent)) return false;

        var throttleKey = $"sepay-poll:{transferContent}";
        if (_cache.TryGetValue(throttleKey, out _))
        {
            _logger.LogDebug("[SePay Polling] Throttled check for content {Content}", transferContent);
            return false;
        }
        _cache.Set(throttleKey, true, ThrottleWindow);

        try
        {
            _logger.LogInformation("[SePay Polling] Starting reconciliation check for content: {Content}", transferContent);
            var ownerToken = await ResolveOwnerApiTokenAsync(transferContent, cancellationToken);
            if (string.IsNullOrWhiteSpace(ownerToken))
            {
                _logger.LogWarning("[SePay Polling] No valid SePay token found for content: {Content}", transferContent);
                return false;
            }

            var found = await _queryClient.FindIncomingTransactionAsync(transferContent, ownerToken, cancellationToken);
            if (found is null)
            {
                _logger.LogInformation("[SePay Polling] No matching transaction found on SePay for content: {Content}", transferContent);
                return false;
            }

            _logger.LogInformation("[SePay Polling] MATCH FOUND on SePay! TxId: {TxId}, Amount: {Amount}, Content: \"{TxContent}\". Forwarding to Webhook Processor...",
                found.Id, found.AmountIn, found.TransactionContent);

            var request = new SePayWebhookRequest
            {
                Id = SyntheticId(found.ReferenceNumber ?? found.Id),
                AccountNumber = found.AccountNumber,
                Code = found.Code,
                Content = found.TransactionContent,
                TransferType = "in",
                TransferAmount = found.AmountIn,
                ReferenceCode = found.ReferenceNumber,
            };
            var result = await _webhookService.Process(request, cancellationToken);
            _logger.LogInformation("[SePay Polling] Webhook processing result for {Content}: Success={Success}, Message={Message}",
                transferContent, result.Success, result.Message);
            return result.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SePay Polling] Reconciliation error for content {Content}", transferContent);
            return false;
        }
    }

    /// <summary>
    /// The money for a booking lands in the venue owner's own SePay account, so the transaction
    /// list has to be queried with that owner's token. Returns null when the owner has not
    /// configured one (or the stored value can no longer be decrypted), which leaves the client
    /// falling back to the platform token.
    /// </summary>
    private async Task<string?> ResolveOwnerApiTokenAsync(string transferContent, CancellationToken cancellationToken)
    {
        var ownerId = await _paymentRepository.Payments.AsNoTracking()
            .Where(payment => payment.TransferContent == transferContent)
            .Select(payment => (int?)payment.Booking.Court.Venue.OwnerId
                ?? (int?)payment.Booking.Slots.Select(s => s.Court.Venue.OwnerId).FirstOrDefault()
                ?? (int?)payment.SessionTicket.TicketSession.Booking.Court.Venue.OwnerId
                ?? (int?)payment.SessionTicket.TicketSession.Booking.Slots.Select(s => s.Court.Venue.OwnerId).FirstOrDefault())
            .FirstOrDefaultAsync(cancellationToken);

        if (!ownerId.HasValue)
        {
            _logger.LogWarning("[SePay Polling] Could not determine OwnerId for transfer content: {Content}", transferContent);
            return null;
        }

        var encryptedToken = await _paymentRepository.OwnerBankAccounts.AsNoTracking()
            .Where(account => account.OwnerId == ownerId.Value && account.IsActive)
            .Select(account => account.SePayApiToken)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(encryptedToken))
        {
            _logger.LogWarning("[SePay Polling] Owner #{OwnerId} has no active SePay token configured.", ownerId.Value);
            return null;
        }

        try
        {
            return _encryptionService.Decrypt(encryptedToken);
        }
        catch (Exception exception) when (exception is CryptographicException or InvalidOperationException)
        {
            _logger.LogWarning(exception,
                "[SePay Polling] Could not decrypt the owner SePay token for Owner #{OwnerId}, content {Content}",
                ownerId.Value, transferContent);
            return null;
        }
    }

    /// <summary>
    /// SePay's v1 webhook "id" is a positive long, already used as the ledger's dedup key.
    /// The v2 transaction-list API instead hands back a UUID, so transactions found this way
    /// are stamped with a hash forced into negative space -- disjoint from every real webhook
    /// id -- to avoid colliding with a genuine webhook delivery in SePayTransactions.
    /// </summary>
    private static long SyntheticId(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        var val = BitConverter.ToInt64(hash, 0) & long.MaxValue;
        return val == 0 ? 1 : val;
    }
}
