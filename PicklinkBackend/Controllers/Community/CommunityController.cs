using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.Services;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public partial class CommunityController : CommunityService
{
    private readonly CommunityDiscoveryService _discoveryService;

    public CommunityController(
        CommunityServiceDependencies dependencies,
        CommunityDiscoveryService discoveryService)
        : base(dependencies.DbContext, dependencies.Notifications)
    {
        _discoveryService = discoveryService;
    }
}
