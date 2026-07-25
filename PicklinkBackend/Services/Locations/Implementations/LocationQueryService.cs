using PicklinkBackend.DTOs;
using PicklinkBackend.Repositories;

namespace PicklinkBackend.Services.Locations.Implementations;

public sealed class LocationQueryService
{
    private readonly IVenueRepository _venueRepository;

    public LocationQueryService(IVenueRepository venueRepository)
    {
        _venueRepository = venueRepository;
    }

    public Task<List<ProvinceResponse>> ListProvincesAsync(CancellationToken cancellationToken) =>
        _venueRepository.ListProvincesAsync(cancellationToken);

    public Task<List<WardResponse>> ListWardsAsync(string provinceCode, CancellationToken cancellationToken) =>
        _venueRepository.ListWardsAsync(provinceCode, cancellationToken);
}
