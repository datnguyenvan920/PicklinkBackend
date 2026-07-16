using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;
using PicklinkBackend.Services.Matches;
using PicklinkBackend.Services.Notifications;
using PicklinkBackend.Services.Shared;

namespace PicklinkBackend.Controllers;

[Authorize]
[ApiController]
[Route("api/matchmaking")]
public class MatchmakingController : ControllerBase
{
    private readonly MatchmakingService _matchmakingService;
    private readonly MatchRealtimeNotifier _matchRealtime;
    private readonly NotificationRealtimeNotifier _notificationRealtime;

    public MatchmakingController(
        MatchmakingService matchmakingService,
        MatchRealtimeNotifier matchRealtime,
        NotificationRealtimeNotifier notificationRealtime)
    {
        _matchmakingService = matchmakingService;
        _matchRealtime = matchRealtime;
        _notificationRealtime = notificationRealtime;
    }

    private void SetCurrentUser() =>
        _matchmakingService.SetCurrentUserId(CurrentUserId());

    private int? CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;

    private ActionResult<T> ToActionResult<T>(ServiceResult<T> result) =>
        result.Status switch
        {
            ServiceResultStatus.Success => Ok(result.Value),
            ServiceResultStatus.Created => CreatedAtAction(result.CreatedActionName, result.CreatedRouteValues, result.Value),
            ServiceResultStatus.NoContent => NoContent(),
            ServiceResultStatus.BadRequest => BadRequest(result.Error),
            ServiceResultStatus.Unauthorized => result.Error is null ? Unauthorized() : Unauthorized(result.Error),
            ServiceResultStatus.Forbidden => result.Error is null ? Forbid() : StatusCode(StatusCodes.Status403Forbidden, result.Error),
            ServiceResultStatus.NotFound => result.Error is null ? NotFound() : NotFound(result.Error),
            ServiceResultStatus.Conflict => Conflict(result.Error),
            ServiceResultStatus.StatusCode => StatusCode(result.RawStatusCode ?? StatusCodes.Status500InternalServerError, result.Value ?? result.Error),
            _ => StatusCode(StatusCodes.Status500InternalServerError, result.Error)
        };

    private IActionResult ToActionResult(ServiceResult result) =>
        result.Status switch
        {
            ServiceResultStatus.Success => result.Value is null ? Ok() : Ok(result.Value),
            ServiceResultStatus.Created => CreatedAtAction(result.CreatedActionName, result.CreatedRouteValues, result.Value),
            ServiceResultStatus.NoContent => NoContent(),
            ServiceResultStatus.BadRequest => BadRequest(result.Error),
            ServiceResultStatus.Unauthorized => result.Error is null ? Unauthorized() : Unauthorized(result.Error),
            ServiceResultStatus.Forbidden => result.Error is null ? Forbid() : StatusCode(StatusCodes.Status403Forbidden, result.Error),
            ServiceResultStatus.NotFound => result.Error is null ? NotFound() : NotFound(result.Error),
            ServiceResultStatus.Conflict => Conflict(result.Error),
            ServiceResultStatus.StatusCode => StatusCode(result.RawStatusCode ?? StatusCodes.Status500InternalServerError, result.Value ?? result.Error),
            _ => StatusCode(StatusCodes.Status500InternalServerError, result.Error)
        };

    [HttpGet("status")]
    public async Task<ActionResult<QueueStatusResponse>> GetQueueStatus(CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _matchmakingService.GetQueueStatus(cancellationToken));
    }

    [HttpPost("join-solo")]
    public async Task<ActionResult<QueueStatusResponse>> JoinSoloQueue(
        [FromBody] JoinSoloQueueRequest request,
        CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _matchmakingService.JoinSoloQueue(request, cancellationToken));
    }

    [HttpPost("join-lobby/{matchId:int}")]
    public async Task<ActionResult<QueueStatusResponse>> JoinLobbyQueue(
        int matchId,
        CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _matchmakingService.JoinLobbyQueue(matchId, cancellationToken));
    }

    [HttpGet("my-queues")]
    public async Task<ActionResult<List<QueueStatusResponse>>> GetMyQueues(CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _matchmakingService.GetMyQueues(cancellationToken));
    }

    [HttpPost("cancel")]
    public async Task<IActionResult> CancelQueue([FromQuery] int? queueId, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _matchmakingService.CancelQueue(queueId, cancellationToken));
    }

    [HttpPost("resume")]
    public async Task<ActionResult<QueueStatusResponse>> ResumeQueue([FromQuery] int? queueId, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _matchmakingService.ResumeQueue(queueId, cancellationToken));
    }

    [AllowAnonymous]
    [HttpGet("public")]
    public async Task<ActionResult<IReadOnlyList<QueueStatusResponse>>> GetPublicQueues(CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _matchmakingService.GetPublicQueues(cancellationToken));
    }

    [HttpPost("public/join/{queueId:int}")]
    public async Task<ActionResult<QueueStatusResponse>> JoinPublicQueue(int queueId, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _matchmakingService.JoinPublicQueue(queueId, cancellationToken));
    }

    [AllowAnonymous]
    [HttpPost("internal/notify-match")]
    public IActionResult NotifyMatch([FromQuery] int matchId, [FromQuery] string action)
    {
        _matchRealtime.Publish(matchId, action);
        return Ok();
    }

    [AllowAnonymous]
    [HttpPost("internal/notify-player")]
    public IActionResult NotifyPlayer([FromQuery] int userId, [FromQuery] int notificationId, [FromQuery] string action)
    {
        _notificationRealtime.Publish(userId, notificationId, action);
        return Ok();
    }
}
