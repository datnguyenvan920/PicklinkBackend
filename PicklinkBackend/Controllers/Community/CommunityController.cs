using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.Services;

namespace PicklinkBackend.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public partial class CommunityController : ControllerBase
{
    private readonly CommunityService _community;
    private readonly CommunityDiscoveryService _discoveryService;
    private readonly CommunityDirectConversationService _directConversations;

    public CommunityController(
        CommunityService community,
        CommunityDiscoveryService discoveryService,
        CommunityDirectConversationService directConversations)
    {
        _community = community;
        _discoveryService = discoveryService;
        _directConversations = directConversations;
    }

    private void SetCommunityUser()
    {
        _community.SetCurrentUserId(GetCurrentUserIdFromClaims());
    }

    private int? GetCurrentUserIdFromClaims()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(userIdClaim, out var userId) ? userId : null;
    }

    private ActionResult<T> ToActionResult<T>(CommunityServiceResult<T> result) =>
        result.Status switch
        {
            CommunityServiceResultStatus.Success => Ok(result.Value),
            CommunityServiceResultStatus.Created => CreatedAtAction(result.CreatedActionName, result.CreatedRouteValues, result.Value),
            CommunityServiceResultStatus.NoContent => NoContent(),
            CommunityServiceResultStatus.BadRequest => BadRequest(result.ErrorBody),
            CommunityServiceResultStatus.Unauthorized => result.ErrorBody is null ? Unauthorized() : Unauthorized(result.ErrorBody),
            CommunityServiceResultStatus.Forbidden => result.ErrorBody is null ? Forbid() : StatusCode(StatusCodes.Status403Forbidden, result.ErrorBody),
            CommunityServiceResultStatus.NotFound => result.ErrorBody is null ? NotFound() : NotFound(result.ErrorBody),
            _ => StatusCode(StatusCodes.Status500InternalServerError, result.ErrorBody)
        };

    private ActionResult ToActionResult(CommunityServiceResult result) =>
        result.Status switch
        {
            CommunityServiceResultStatus.Success => Ok(),
            CommunityServiceResultStatus.Created => CreatedAtAction(result.CreatedActionName, result.CreatedRouteValues, result.Value),
            CommunityServiceResultStatus.NoContent => NoContent(),
            CommunityServiceResultStatus.BadRequest => BadRequest(result.ErrorBody),
            CommunityServiceResultStatus.Unauthorized => result.ErrorBody is null ? Unauthorized() : Unauthorized(result.ErrorBody),
            CommunityServiceResultStatus.Forbidden => result.ErrorBody is null ? Forbid() : StatusCode(StatusCodes.Status403Forbidden, result.ErrorBody),
            CommunityServiceResultStatus.NotFound => result.ErrorBody is null ? NotFound() : NotFound(result.ErrorBody),
            _ => StatusCode(StatusCodes.Status500InternalServerError, result.ErrorBody)
        };

    private ActionResult<T> ToActionResult<T>(DirectConversationServiceResult<T> result) =>
        result.Status switch
        {
            DirectConversationServiceResultStatus.Success => Ok(result.Value),
            DirectConversationServiceResultStatus.BadRequest => BadRequest(new { message = result.ErrorMessage }),
            DirectConversationServiceResultStatus.Unauthorized => Unauthorized(),
            DirectConversationServiceResultStatus.Forbidden => Forbid(),
            DirectConversationServiceResultStatus.NotFound => NotFound(new { message = result.ErrorMessage }),
            _ => StatusCode(StatusCodes.Status500InternalServerError)
        };
}