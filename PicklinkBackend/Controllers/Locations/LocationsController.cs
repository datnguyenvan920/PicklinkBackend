using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace PicklinkBackend.Controllers.Locations;

[ApiController]
[Route("api/locations")]
public sealed class LocationsController : ControllerBase
{
    private const string SourceBaseUrl = "https://provinces.open-api.vn/api/v2/p/";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(12);
    private static readonly string[] ProvincePrefixes = ["Thành phố ", "Tỉnh "];
    private static readonly string[] WardPrefixes = ["Phường ", "Xã ", "Thị trấn "];

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;

    public LocationsController(IHttpClientFactory httpClientFactory, IMemoryCache cache)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
    }

    [HttpGet("provinces")]
    public async Task<ActionResult<IReadOnlyList<ProvinceOption>>> GetProvinces(CancellationToken cancellationToken)
    {
        try
        {
            var provinces = await _cache.GetOrCreateAsync(
                "locations:v2:provinces",
                async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = CacheDuration;

                    var sourceProvinces = await CreateClient()
                        .GetFromJsonAsync<List<LocationProvince>>(SourceBaseUrl, cancellationToken);

                    return (sourceProvinces ?? [])
                        .Select(ToProvinceOption)
                        .OrderBy(province => province.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                });

            return Ok(provinces ?? []);
        }
        catch (HttpRequestException)
        {
            return Problem(
                title: "Không thể tải danh sách tỉnh/thành.",
                detail: "Nguồn dữ liệu địa giới đang không phản hồi. Vui lòng thử lại sau.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    [HttpGet("provinces/{provinceCode}/wards")]
    public async Task<ActionResult<IReadOnlyList<WardOption>>> GetWards(
        string provinceCode,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(provinceCode, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
        {
            return BadRequest("Mã tỉnh/thành không hợp lệ.");
        }

        LocationProvince? province;
        try
        {
            province = await _cache.GetOrCreateAsync(
                $"locations:v2:province:{code}:wards",
                async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = CacheDuration;

                    return await CreateClient()
                        .GetFromJsonAsync<LocationProvince>(
                            $"{SourceBaseUrl}{code.ToString(CultureInfo.InvariantCulture)}?depth=2",
                            cancellationToken);
                });
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return NotFound("Không tìm thấy tỉnh/thành được yêu cầu.");
        }
        catch (HttpRequestException)
        {
            return Problem(
                title: "Không thể tải danh sách xã/phường.",
                detail: "Nguồn dữ liệu địa giới đang không phản hồi. Vui lòng thử lại sau.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (province is null)
        {
            return NotFound("Không tìm thấy tỉnh/thành được yêu cầu.");
        }

        return Ok(province.Wards
            .Select(ToWardOption)
            .OrderBy(ward => ward.Name, StringComparer.OrdinalIgnoreCase)
            .ToList());
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(10);
        return client;
    }

    private static ProvinceOption ToProvinceOption(LocationProvince province) => new(
        province.Code.ToString(CultureInfo.InvariantCulture),
        RemoveAdministrativePrefix(province.Name, ProvincePrefixes),
        province.Name);

    private static WardOption ToWardOption(LocationWard ward) => new(
        ward.Code.ToString(CultureInfo.InvariantCulture),
        ward.ProvinceCode.ToString(CultureInfo.InvariantCulture),
        RemoveAdministrativePrefix(ward.Name, WardPrefixes),
        ward.Name);

    private static string RemoveAdministrativePrefix(string name, IReadOnlyList<string> prefixes)
    {
        var trimmedName = name.Trim();
        foreach (var prefix in prefixes)
        {
            if (trimmedName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return trimmedName[prefix.Length..].Trim();
            }
        }

        return trimmedName;
    }

    public sealed record ProvinceOption(string Code, string Name, string FullName);

    public sealed record WardOption(string Code, string ProvinceCode, string Name, string FullName);

    private sealed record LocationProvince(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("code")] int Code,
        [property: JsonPropertyName("wards")] IReadOnlyList<LocationWard> Wards);

    private sealed record LocationWard(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("code")] int Code,
        [property: JsonPropertyName("province_code")] int ProvinceCode);
}
