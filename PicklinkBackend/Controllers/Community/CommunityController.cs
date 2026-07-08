using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.Services;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public partial class CommunityController : CommunityService
{
    public CommunityController(CommunityServiceDependencies dependencies)
        : base(dependencies.DbContext, dependencies.Notifications)
    {
    }
}