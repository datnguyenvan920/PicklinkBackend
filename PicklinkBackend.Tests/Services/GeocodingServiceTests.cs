using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using PicklinkBackend.Services.Locations;

namespace PicklinkBackend.Tests.Services;

public sealed class GeocodingServiceTests
{
    [Fact]
    public async Task ReverseAsyncReadsVietnamProvinceAndWardFromJsonV2Address()
    {
        const string responseJson = """
            {
              "display_name": "Đường Lê Thánh Tôn, Phường Bến Nghé, Thành phố Hồ Chí Minh, Việt Nam",
              "address": {
                "road": "Đường Lê Thánh Tôn",
                "suburb": "Phường Bến Nghé",
                "city": "Thành phố Hồ Chí Minh",
                "country": "Việt Nam",
                "country_code": "vn"
              }
            }
            """;
        var handler = new StubHttpMessageHandler(responseJson);
        var factory = new StubHttpClientFactory(handler);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Geocoding:NominatimBaseUrl"] = "https://nominatim.test"
            })
            .Build();
        var service = new GeocodingService(factory, cache, configuration);

        var result = await service.ReverseAsync(10.7769, 106.7009, CancellationToken.None);

        Assert.Equal("Đường Lê Thánh Tôn, Phường Bến Nghé, Thành phố Hồ Chí Minh, Việt Nam", result.DisplayName);
        Assert.Equal("Hồ Chí Minh", result.Province);
        Assert.Equal("Bến Nghé", result.Ward);
        Assert.NotNull(handler.LastRequestUri);
        Assert.Contains("format=jsonv2", handler.LastRequestUri.Query);
        Assert.Contains("accept-language=vi", handler.LastRequestUri.Query);
    }

    private sealed class StubHttpClientFactory(StubHttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class StubHttpMessageHandler(string responseJson) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });
        }
    }
}
