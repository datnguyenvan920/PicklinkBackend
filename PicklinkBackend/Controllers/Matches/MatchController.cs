using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.Services;

namespace PicklinkBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
public partial class MatchController : MatchService
{
    public MatchController(MatchServiceDependencies dependencies)
        : base(
            dependencies.Db,
            dependencies.Configuration,
            dependencies.ScheduleRealtime,
            dependencies.MatchRealtime,
            dependencies.Notifications,
            dependencies.PlayerScheduleConflict)
    {
    }
}