using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using PicklinkBackend.Services.Payments.Implementations;

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
    private readonly IMemoryCache _cache;
    private readonly ILogger<SePayReconciliationService> _logger;

    public SePayReconciliationService(
        ISePayTransactionQueryClient queryClient,
        SePayWebhookService webhookService,
        IMemoryCache cache,
        ILogger<SePayReconciliationService> logger)
    {
        _queryClient = queryClient;
        _webhookService = webhookService;
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
        if (_cache.TryGetValue(throttleKey, out _)) return false;
        _cache.Set(throttleKey, true, ThrottleWindow);

        try
        {
            var found = await _queryClient.FindIncomingTransactionAsync(transferContent, cancellationToken);
            if (found is null) return false;

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
            return result.Success;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SePay reconciliation failed for content {Content}", transferContent);
            return false;
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
        return BitConverter.ToInt64(hash, 0) | long.MinValue;
    }
}
