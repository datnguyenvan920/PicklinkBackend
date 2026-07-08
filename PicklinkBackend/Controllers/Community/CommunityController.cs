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
    private readonly CommunityDirectConversationService _directConversations;

    public CommunityController(
        CommunityServiceDependencies dependencies,
        CommunityDiscoveryService discoveryService,
        CommunityDirectConversationService directConversations)
        : base(dependencies.DbContext, dependencies.Notifications)
    {
        _discoveryService = discoveryService;
        _directConversations = directConversations;
    }
}
