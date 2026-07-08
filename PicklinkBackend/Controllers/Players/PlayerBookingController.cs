using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.Services;

namespace PicklinkBackend.Controllers;

[ApiController]
[Route("api/player-bookings")]
public class PlayerBookingController : PlayerBookingService
{
    public PlayerBookingController(PlayerBookingServiceDependencies dependencies)
        : base(
            dependencies.DbContext,
            dependencies.Configuration,
            dependencies.ScheduleRealtime,
            dependencies.PlayerScheduleConflict)
    {
    }
}