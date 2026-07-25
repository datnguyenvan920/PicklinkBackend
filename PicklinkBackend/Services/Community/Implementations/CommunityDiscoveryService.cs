using PicklinkBackend.DTOs;
using PicklinkBackend.Repositories;

namespace PicklinkBackend.Services.Community.Implementations;

public class CommunityDiscoveryService
{
    private readonly ICommunityRepository _communityRepository;

    public CommunityDiscoveryService(ICommunityRepository communityRepository)
    {
        _communityRepository = communityRepository;
    }

    public Task<IReadOnlyList<OutstandingPlayerResponse>> GetOutstandingPlayersAsync(
        CancellationToken cancellationToken)
    {
        return _communityRepository.GetOutstandingPlayersAsync(cancellationToken);
    }
}
