using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.Services;

namespace PicklinkBackend.Controllers;

[ApiController]
[Route("api/[controller]")]
public partial class MatchController : ControllerBase
{
    private readonly MatchService _matchService;

    public MatchController(MatchService matchService)
    {
        _matchService = matchService;
    }

    private void SetCurrentUser() =>
        _matchService.SetCurrentUserId(CurrentUserId());

    private int? CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;

    private ActionResult<T> ToActionResult<T>(MatchServiceResult<T> result) =>
        result.Status switch
        {
            MatchServiceResultStatus.Success => Ok(result.Value),
            MatchServiceResultStatus.Created => CreatedAtAction(result.CreatedActionName, result.CreatedRouteValues, result.Value),
            MatchServiceResultStatus.NoContent => NoContent(),
            MatchServiceResultStatus.BadRequest => BadRequest(result.Error),
            MatchServiceResultStatus.Unauthorized => result.Error is null ? Unauthorized() : Unauthorized(result.Error),
            MatchServiceResultStatus.Forbidden => result.Error is null ? Forbid() : StatusCode(StatusCodes.Status403Forbidden, result.Error),
            MatchServiceResultStatus.NotFound => result.Error is null ? NotFound() : NotFound(result.Error),
            MatchServiceResultStatus.Conflict => Conflict(result.Error),
            MatchServiceResultStatus.StatusCode => StatusCode(result.RawStatusCode ?? StatusCodes.Status500InternalServerError, result.Value ?? result.Error),
            _ => StatusCode(StatusCodes.Status500InternalServerError, result.Error)
        };

    private IActionResult ToActionResult(MatchServiceResult result) =>
        result.Status switch
        {
            MatchServiceResultStatus.Success => result.Value is null ? Ok() : Ok(result.Value),
            MatchServiceResultStatus.Created => CreatedAtAction(result.CreatedActionName, result.CreatedRouteValues, result.Value),
            MatchServiceResultStatus.NoContent => NoContent(),
            MatchServiceResultStatus.BadRequest => BadRequest(result.Error),
            MatchServiceResultStatus.Unauthorized => result.Error is null ? Unauthorized() : Unauthorized(result.Error),
            MatchServiceResultStatus.Forbidden => result.Error is null ? Forbid() : StatusCode(StatusCodes.Status403Forbidden, result.Error),
            MatchServiceResultStatus.NotFound => result.Error is null ? NotFound() : NotFound(result.Error),
            MatchServiceResultStatus.Conflict => Conflict(result.Error),
            MatchServiceResultStatus.StatusCode => StatusCode(result.RawStatusCode ?? StatusCodes.Status500InternalServerError, result.Value ?? result.Error),
            _ => StatusCode(StatusCodes.Status500InternalServerError, result.Error)
        };
}