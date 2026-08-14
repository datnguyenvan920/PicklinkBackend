using System.Net.Http.Headers;
using System.Text.Json.Serialization;

namespace PicklinkBackend.Services.Payments.Implementations;

public sealed class SePayTransactionQueryClient : ISePayTransactionQueryClient
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SePayTransactionQueryClient> _logger;

    public SePayTransactionQueryClient(HttpClient httpClient, IConfiguration configuration, ILogger<SePayTransactionQueryClient> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<SePayListedTransaction?> FindIncomingTransactionAsync(string transferContent, CancellationToken cancellationToken)
    {
        var apiToken = _configuration["SePay:ApiToken"];
        if (string.IsNullOrWhiteSpace(apiToken) || string.IsNullOrWhiteSpace(transferContent))
            return null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"v2/transactions?transaction_content={Uri.EscapeDataString(transferContent)}&transfer_type=in&per_page=5");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("SePay transaction list returned {StatusCode} for content {Content}",
                    (int)response.StatusCode, transferContent);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<SePayTransactionListResponse>(cancellationToken);
            var match = payload?.Data?.FirstOrDefault(item =>
                item.TransactionContent.Contains(transferContent, StringComparison.OrdinalIgnoreCase)
                || (item.Code?.Contains(transferContent, StringComparison.OrdinalIgnoreCase) ?? false));
            if (match is null) return null;

            return new SePayListedTransaction(match.Id, match.AccountNumber, match.Code, match.TransactionContent, match.AmountIn, match.ReferenceNumber);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            _logger.LogWarning(ex, "SePay transaction list lookup failed for content {Content}", transferContent);
            return null;
        }
    }

    private sealed class SePayTransactionListResponse
    {
        [JsonPropertyName("data")] public List<SePayApiTransaction>? Data { get; set; }
    }

    private sealed class SePayApiTransaction
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("account_number")] public string AccountNumber { get; set; } = string.Empty;
        [JsonPropertyName("code")] public string? Code { get; set; }
        [JsonPropertyName("transaction_content")] public string TransactionContent { get; set; } = string.Empty;
        [JsonPropertyName("amount_in")] public decimal AmountIn { get; set; }
        [JsonPropertyName("reference_number")] public string? ReferenceNumber { get; set; }
    }
}
