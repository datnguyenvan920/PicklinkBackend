using Microsoft.Extensions.Caching.Memory;
using PicklinkBackend.DTOs;
using PicklinkBackend.Repositories;

namespace PicklinkBackend.Services.Locations.Implementations;

public sealed class LocationQueryService
{
    // Administrative divisions change at most a few times per decade, but every page that renders
    // an address filter re-requests them. Caching removes a database round trip from those pages.
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(12);

    private readonly IVenueRepository _venueRepository;
    private readonly IMemoryCache _cache;

    public LocationQueryService(IVenueRepository venueRepository, IMemoryCache cache)
    {
        _venueRepository = venueRepository;
        _cache = cache;
    }

    public async Task<List<ProvinceResponse>> ListProvincesAsync(CancellationToken cancellationToken)
    {
        var provinces = await GetOrCreateAsync(
            "locations:provinces",
            () => _venueRepository.ListProvincesAsync(cancellationToken));

        return [.. provinces];
    }

    public async Task<List<WardResponse>> ListWardsAsync(string provinceCode, CancellationToken cancellationToken)
    {
        var wards = await GetOrCreateAsync(
            $"locations:wards:{provinceCode}",
            () => _venueRepository.ListWardsAsync(provinceCode, cancellationToken));

        return [.. wards];
    }

    private async Task<List<T>> GetOrCreateAsync<T>(string cacheKey, Func<Task<List<T>>> load)
    {
        if (_cache.TryGetValue(cacheKey, out List<T>? cached) && cached is not null)
        {
            return cached;
        }

        var loaded = await load();
        _cache.Set(cacheKey, loaded, CacheLifetime);
        return loaded;
    }
}
