using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.Services;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize(Roles = "VenueOwner")]
[Route("api/owner")]
public class OwnerVenueController : OwnerVenueService
{
    public OwnerVenueController(OwnerVenueServiceDependencies dependencies)
        : base(
            dependencies.DbContext,
            dependencies.Environment,
            dependencies.ScheduleRealtime,
            dependencies.VenueRealtime)
    {
    }
}