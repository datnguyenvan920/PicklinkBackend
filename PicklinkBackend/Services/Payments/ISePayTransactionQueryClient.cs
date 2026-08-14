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
    Task<SePayListedTransaction?> FindIncomingTransactionAsync(string transferContent, CancellationToken cancellationToken);
}
