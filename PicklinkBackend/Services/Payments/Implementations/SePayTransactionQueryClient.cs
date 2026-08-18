using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PicklinkBackend.Services.Payments.Implementations;

public sealed class SePayTransactionQueryClient : ISePayTransactionQueryClient
{
    private static readonly Regex PrefixPattern = new(@"^(PL[A-Z0-9]?)-?([A-Z0-9]+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PlTokenPattern = new(@"(PL[A-Z0-9]{4,})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SePayTransactionQueryClient> _logger;

    public SePayTransactionQueryClient(HttpClient httpClient, IConfiguration configuration, ILogger<SePayTransactionQueryClient> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<SePayListedTransaction?> FindIncomingTransactionAsync(
        string transferContent,
        string? apiToken,
        CancellationToken cancellationToken)
    {
        var effectiveToken = string.IsNullOrWhiteSpace(apiToken)
            ? _configuration["SePay:ApiToken"]
            : apiToken.Trim();
        if (string.IsNullOrWhiteSpace(effectiveToken) || string.IsNullOrWhiteSpace(transferContent))
            return null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "transactions?limit=20");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", effectiveToken);

            _logger.LogInformation("[SePay Query] Sending GET https://userapi.sepay.vn/v2/transactions?limit=20 for content: {Content}", transferContent);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[SePay Query] SePay transaction list returned HTTP {StatusCode} for content {Content}",
                    (int)response.StatusCode, transferContent);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<SePayTransactionListResponse>(cancellationToken);
            var transactions = payload?.Transactions ?? payload?.Data ?? [];
            _logger.LogInformation("[SePay Query] SePay returned {Count} recent transactions. Checking for match with \"{Content}\"...",
                transactions.Count, transferContent);

            if (transactions.Count == 0) return null;

            var targetRaw = transferContent.Trim().ToUpperInvariant();
            var targetNoDash = targetRaw.Replace("-", "").Replace(" ", "");

            string? prefix = null;
            string? codePart = null;
            var matchPrefix = PrefixPattern.Match(targetRaw);
            if (matchPrefix.Success)
            {
                prefix = matchPrefix.Groups[1].Value.ToUpperInvariant();
                codePart = matchPrefix.Groups[2].Value.ToUpperInvariant();
            }

            var match = transactions.FirstOrDefault(item =>
            {
                var itemContent = (item.TransactionContent ?? string.Empty).ToUpperInvariant();
                var itemCode = (item.Code ?? string.Empty).ToUpperInvariant();
                var itemCombined = $"{itemCode} {itemContent}";
                var itemCombinedNoDash = itemCombined.Replace("-", "").Replace(" ", "");

                // 1. Exact raw or no-dash match
                if (itemCombined.Contains(targetRaw, StringComparison.OrdinalIgnoreCase) ||
                    itemCombinedNoDash.Contains(targetNoDash, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // 2. Both prefix (e.g. PLG) and codePart (e.g. B36F2B910F674038) match anywhere in transaction content
                if (!string.IsNullOrWhiteSpace(prefix) && !string.IsNullOrWhiteSpace(codePart))
                {
                    if (itemCombined.Contains(prefix, StringComparison.OrdinalIgnoreCase) &&
                        itemCombined.Contains(codePart, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    if (itemCombinedNoDash.Contains($"{prefix}{codePart}", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                // 3. Extracted PL token match
                var extractedTokens = PlTokenPattern.Matches(itemCombined);
                foreach (Match m in extractedTokens)
                {
                    var tokenVal = m.Value.ToUpperInvariant();
                    if (tokenVal == targetNoDash || tokenVal == targetRaw)
                        return true;
                }

                return false;
            });

            if (match is null)
            {
                _logger.LogInformation("[SePay Query] No match found in the latest {Count} SePay transactions for \"{Content}\".",
                    transactions.Count, transferContent);
                return null;
            }

            _logger.LogInformation("[SePay Query] Matched SePay transaction! ID: {TxId}, Amount: {Amount}, Content: \"{TxContent}\"",
                match.Id, match.AmountIn, match.TransactionContent);

            return new SePayListedTransaction(
                match.Id,
                match.AccountNumber,
                match.Code,
                match.TransactionContent,
                match.AmountIn,
                match.ReferenceNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SePay Query] SePay transaction list lookup failed for content {Content}: {Message}", transferContent, ex.Message);
            return null;
        }
    }

    private sealed class SePayTransactionListResponse
    {
        [JsonPropertyName("status")] public System.Text.Json.JsonElement Status { get; set; }
        [JsonPropertyName("transactions")] public List<SePayApiTransaction>? Transactions { get; set; }
        [JsonPropertyName("data")] public List<SePayApiTransaction>? Data { get; set; }
    }

    private sealed class SePayApiTransaction
    {
        [JsonPropertyName("id")] public System.Text.Json.JsonElement IdElement { get; set; }
        [JsonPropertyName("account_number")] public string? AccountNumber { get; set; }
        [JsonPropertyName("code")] public string? Code { get; set; }
        [JsonPropertyName("transaction_content")] public string? TransactionContent { get; set; }
        [JsonPropertyName("amount_in")] public System.Text.Json.JsonElement AmountInElement { get; set; }
        [JsonPropertyName("reference_number")] public string? ReferenceNumber { get; set; }

        [JsonIgnore]
        public string Id => IdElement.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => IdElement.GetString() ?? string.Empty,
            System.Text.Json.JsonValueKind.Number => IdElement.GetInt64().ToString(),
            _ => string.Empty
        };

        [JsonIgnore]
        public decimal AmountIn => AmountInElement.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Number => AmountInElement.GetDecimal(),
            System.Text.Json.JsonValueKind.String => decimal.TryParse(AmountInElement.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m,
            _ => 0m
        };
    }
}
