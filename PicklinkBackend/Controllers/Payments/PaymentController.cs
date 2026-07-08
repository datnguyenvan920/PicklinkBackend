using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.Services;

namespace PicklinkBackend.Controllers;

[ApiController]
[Route("api/payments")]
[Authorize]
public class PaymentController : PaymentService
{
    public PaymentController(PaymentServiceDependencies dependencies)
        : base(
            dependencies.DbContext,
            dependencies.Environment,
            dependencies.ScheduleRealtime,
            dependencies.PaymentRealtime,
            dependencies.MatchRealtime,
            dependencies.Notifications)
    {
    }
}