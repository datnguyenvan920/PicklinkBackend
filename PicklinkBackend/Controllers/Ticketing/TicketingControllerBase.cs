using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.Services.Shared;

namespace PicklinkBackend.Controllers;

public abstract class TicketingControllerBase : ControllerBase
{
    protected int? CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    protected ActionResult<T> ToActionResult<T>(ServiceResult<T> result) =>
        result.Status switch
        {
            ServiceResultStatus.Success => Ok(result.Value),
            ServiceResultStatus.Created => StatusCode(StatusCodes.Status201Created, result.Value),
            ServiceResultStatus.NoContent => NoContent(),
            ServiceResultStatus.BadRequest => BadRequest(result.Error),
            ServiceResultStatus.Unauthorized => result.Error is null ? Unauthorized() : Unauthorized(result.Error),
            ServiceResultStatus.Forbidden => StatusCode(StatusCodes.Status403Forbidden, result.Error),
            ServiceResultStatus.NotFound => NotFound(result.Error),
            ServiceResultStatus.Conflict => Conflict(result.Error),
            ServiceResultStatus.StatusCode => StatusCode(
                result.RawStatusCode ?? StatusCodes.Status500InternalServerError,
                result.Value ?? result.Error),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
}
