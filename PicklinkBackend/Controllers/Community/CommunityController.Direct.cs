using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;

namespace PicklinkBackend.Controllers;

public partial class CommunityController
{
    [HttpGet("players/outstanding")]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<OutstandingPlayerResponse>>> GetOutstandingPlayers(
        CancellationToken cancellationToken)
    {
        return Ok(await _discoveryService.GetOutstandingPlayersAsync(cancellationToken));
    }
}
