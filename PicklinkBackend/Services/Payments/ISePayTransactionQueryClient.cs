namespace PicklinkBackend.Services.Payments;

public sealed record SePayListedTransaction(
    string Id,
    string AccountNumber,
    string? Code,
    string TransactionContent,
    decimal AmountIn,
    string? ReferenceNumber);

public interface ISePayTransactionQueryClient
{
    /// <summary>
    /// Queries SePay's transaction list for an incoming transfer carrying <paramref name="transferContent"/>.
    /// <paramref name="apiToken"/> is the venue owner's own decrypted SePay token, since the money lands in
    /// their account; when it is null the platform-wide token from configuration is used instead.
    /// </summary>
    Task<SePayListedTransaction?> FindIncomingTransactionAsync(
        string transferContent,
        string? apiToken,
        CancellationToken cancellationToken);
}
