using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using PicklinkBackend.DTOs;

namespace PicklinkBackend.Services.Locations;

public sealed class GeocodingService
{
    private const string UserAgent = "PicklinkBackend/1.0 (+https://picklink.vercel.app)";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);
    private static readonly TimeSpan MinimumOutboundInterval = TimeSpan.FromSeconds(1);
    private static readonly SemaphoreSlim OutboundGate = new(1, 1);
    private static long _lastOutboundTimestamp;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;
    private readonly Uri _baseUri;

    public GeocodingService(
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;

        var configuredBaseUrl = configuration["Geocoding:NominatimBaseUrl"];
        if (!Uri.TryCreate(configuredBaseUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Geocoding:NominatimBaseUrl must be an absolute HTTP(S) URL.");
        }

        _baseUri = new Uri($"{baseUri.ToString().TrimEnd('/')}/");
    }

    public Task<GeocodeCoordinatesResponse?> ForwardAsync(
        string province,
        string? ward,
        CancellationToken cancellationToken)
    {
        var location = string.Join(", ", new[] { ward, province, "Việt Nam" }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim()));
        var cacheKey = $"geocoding:forward:{location.ToUpperInvariant()}";
        var path = $"search?format=jsonv2&q={Uri.EscapeDataString(location)}&limit=1&countrycodes=vn&addressdetails=1";

        return GetOrFetchAsync(cacheKey, path, ParseFirstCoordinate, cancellationToken);
    }

    public Task<ReverseGeocodeResponse> ReverseAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken)
    {
        var latitudeText = latitude.ToString("R", CultureInfo.InvariantCulture);
        var longitudeText = longitude.ToString("R", CultureInfo.InvariantCulture);
        var cacheKey = $"geocoding:reverse:{latitudeText}:{longitudeText}";
        var path = $"reverse?format=geocodejson&lat={latitudeText}&lon={longitudeText}&zoom=18&addressdetails=1";

        return GetOrFetchAsync(cacheKey, path, ParseReverseResult, cancellationToken);
    }

    public Task<List<GeocodingSearchResultResponse>> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var normalizedQuery = query.Trim();
        var cacheKey = $"geocoding:search:{normalizedQuery.ToUpperInvariant()}";
        var path = $"search?format=jsonv2&q={Uri.EscapeDataString(normalizedQuery)}&limit=5&countrycodes=vn&addressdetails=1";

        return GetOrFetchAsync(cacheKey, path, ParseSearchResults, cancellationToken);
    }

    private async Task<T> GetOrFetchAsync<T>(
        string cacheKey,
        string path,
        Func<JsonElement, T> parse,
        CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(cacheKey, out CachedValue<T>? cached) && cached is not null)
        {
            return cached.Value;
        }

        // ponytail: one process-wide gate satisfies Nominatim's shared 1 request/second limit.
        await OutboundGate.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue(cacheKey, out cached) && cached is not null)
            {
                return cached.Value;
            }

            if (_lastOutboundTimestamp != 0)
            {
                var remaining = MinimumOutboundInterval - Stopwatch.GetElapsedTime(_lastOutboundTimestamp);
                if (remaining > TimeSpan.Zero)
                {
                    await Task.Delay(remaining, cancellationToken);
                }
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseUri, path));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue("vi"));
            request.Headers.UserAgent.ParseAdd(UserAgent);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            _lastOutboundTimestamp = Stopwatch.GetTimestamp();

            try
            {
                using var client = _httpClientFactory.CreateClient(nameof(GeocodingService));
                using var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token);
                if (!response.IsSuccessStatusCode)
                {
                    throw new GeocodingServiceException("The geocoding provider is temporarily unavailable.");
                }

                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                using var document = await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: timeout.Token);
                var value = parse(document.RootElement);
                _cache.Set(cacheKey, new CachedValue<T>(value), CacheDuration);
                return value;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new GeocodingServiceException("The geocoding provider timed out.");
            }
            catch (HttpRequestException)
            {
                throw new GeocodingServiceException("The geocoding provider is temporarily unavailable.");
            }
            catch (JsonException)
            {
                throw new GeocodingServiceException("The geocoding provider returned an invalid response.");
            }
        }
        finally
        {
            OutboundGate.Release();
        }
    }

    private static GeocodeCoordinatesResponse? ParseFirstCoordinate(JsonElement root)
    {
        EnsureArray(root);
        foreach (var item in root.EnumerateArray())
        {
            if (IsVietnamResult(item) && TryReadCoordinates(item, out var latitude, out var longitude))
            {
                return new GeocodeCoordinatesResponse(latitude, longitude);
            }
        }

        return null;
    }

    private static List<GeocodingSearchResultResponse> ParseSearchResults(JsonElement root)
    {
        EnsureArray(root);
        var results = new List<GeocodingSearchResultResponse>(5);
        foreach (var item in root.EnumerateArray())
        {
            var placeId = ReadInt64(item, "place_id");
            var displayName = ReadString(item, "display_name");
            if (placeId <= 0
                || displayName.Length == 0
                || !IsVietnamResult(item)
                || !TryReadCoordinates(item, out var latitude, out var longitude))
            {
                continue;
            }

            results.Add(new GeocodingSearchResultResponse(placeId, displayName, latitude, longitude));
            if (results.Count == 5) break;
        }

        return results;
    }

    private static ReverseGeocodeResponse ParseReverseResult(JsonElement root)
    {
        if (!root.TryGetProperty("features", out var features)
            || features.ValueKind != JsonValueKind.Array
            || features.GetArrayLength() == 0)
        {
            return EmptyReverseResult();
        }

        var feature = features[0];
        if (!feature.TryGetProperty("properties", out var properties)
            || !properties.TryGetProperty("geocoding", out var geocoding)
            || !ReadString(geocoding, "country_code").Equals("vn", StringComparison.OrdinalIgnoreCase))
        {
            return EmptyReverseResult();
        }

        var province = string.Empty;
        var ward = string.Empty;
        if (geocoding.TryGetProperty("admin", out var admin))
        {
            province = ReadString(admin, "level4");
            ward = ReadString(admin, "level6");
        }

        province = NormalizeAdministrativeName(
            province.Length > 0 ? province : FirstNonEmpty(geocoding, "city", "state"));
        ward = NormalizeAdministrativeName(
            ward.Length > 0 ? ward : FirstNonEmpty(geocoding, "district", "locality"));

        return new ReverseGeocodeResponse(
            ReadString(geocoding, "label"),
            province,
            ward);
    }

    private static bool IsVietnamResult(JsonElement item) =>
        item.TryGetProperty("address", out var address)
        && ReadString(address, "country_code").Equals("vn", StringComparison.OrdinalIgnoreCase);

    private static bool TryReadCoordinates(
        JsonElement item,
        out double latitude,
        out double longitude)
    {
        latitude = 0;
        longitude = 0;
        var valid = double.TryParse(
                ReadString(item, "lat"),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out latitude)
            && double.TryParse(
                ReadString(item, "lon"),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out longitude)
            && double.IsFinite(latitude)
            && double.IsFinite(longitude)
            && latitude is >= -90 and <= 90
            && longitude is >= -180 and <= 180;
        return valid;
    }

    private static string FirstNonEmpty(JsonElement item, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var value = ReadString(item, propertyName);
            if (value.Length > 0) return value;
        }

        return string.Empty;
    }

    private static string ReadString(JsonElement item, string propertyName) =>
        item.ValueKind == JsonValueKind.Object
        && item.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty
            : string.Empty;

    private static long ReadInt64(JsonElement item, string propertyName) =>
        item.ValueKind == JsonValueKind.Object
        && item.TryGetProperty(propertyName, out var value)
        && value.TryGetInt64(out var result)
            ? result
            : 0;

    private static string NormalizeAdministrativeName(string value)
    {
        string[] prefixes =
        [
            "Thành phố ", "Tỉnh ", "TP. ", "TP ", "Phường ", "Xã ", "Thị trấn ",
            "Đặc khu ", "Quận ", "Huyện ", "Thị xã "
        ];
        var trimmed = value.Trim();
        var prefix = prefixes.FirstOrDefault(candidate =>
            trimmed.StartsWith(candidate, StringComparison.OrdinalIgnoreCase));
        return prefix is null ? trimmed : trimmed[prefix.Length..].Trim();
    }

    private static void EnsureArray(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Expected a JSON array.");
        }
    }

    private static ReverseGeocodeResponse EmptyReverseResult() => new(string.Empty, string.Empty, string.Empty);

    private sealed record CachedValue<T>(T Value);
}

public sealed class GeocodingServiceException(string message) : Exception(message);
