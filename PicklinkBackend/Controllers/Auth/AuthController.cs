using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.Services.Auth;

namespace PicklinkBackend.Controllers;

[ApiController]
[Route("api/auth")]
public partial class AuthController : ControllerBase
{
    private readonly AuthService _authService;

    public AuthController(AuthService authService)
    {
        _authService = authService;
    }

    private ActionResult<T> ToActionResult<T>(AuthServiceResult<T> result) =>
        result.Status switch
        {
            AuthServiceResultStatus.Success => Ok(result.Value),
            AuthServiceResultStatus.BadRequest => BadRequest(new { message = result.ErrorMessage }),
            AuthServiceResultStatus.Unauthorized => string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? Unauthorized()
                : Unauthorized(new { message = result.ErrorMessage }),
            AuthServiceResultStatus.Forbidden => Forbid(),
            AuthServiceResultStatus.NotFound => string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? NotFound()
                : NotFound(new { message = result.ErrorMessage }),
            AuthServiceResultStatus.Conflict => Conflict(new { message = result.ErrorMessage }),
            AuthServiceResultStatus.ServerError => StatusCode(StatusCodes.Status500InternalServerError, new { message = result.ErrorMessage }),
            AuthServiceResultStatus.Problem => Problem(title: result.Title, detail: result.ErrorMessage),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };

    private int? CurrentUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}