using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PicklinkBackend.DTOs;

namespace PicklinkBackend.Controllers;

public partial class MatchController
{
    [Authorize]
    [HttpGet("player-recommendations")]
    public async Task<ActionResult<List<MatchPlayerRecommendationResponse>>> GetPlayerRecommendations(
        double radiusKm = 5,
        double? latitude = null,
        double? longitude = null,
        string? province = null,
        string? ward = null,
        int minSkillLevel = 1,
        int maxSkillLevel = 5,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        SetCurrentUser();
        return ToActionResult(await _matchService.GetPlayerRecommendations(radiusKm, latitude, longitude, province, ward, minSkillLevel, maxSkillLevel, limit, cancellationToken));
    }

    [Authorize]
    [HttpPost("{matchId:int}/invitations")]
    public async Task<ActionResult<OpenMatchDetailResponse>> InviteMatchPlayers(
        int matchId,
        InviteMatchPlayersRequest request,
        CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _matchService.InviteMatchPlayers(matchId, request, cancellationToken));
    }

    [Authorize]
    [HttpPost("{matchId:int}/invitation/accept")]
    public async Task<ActionResult<OpenMatchDetailResponse>> AcceptMatchInvitation(int matchId, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _matchService.AcceptMatchInvitation(matchId, cancellationToken));
    }

    [Authorize]
    [HttpPost("{matchId:int}/invitation/decline")]
    public async Task<ActionResult<OpenMatchDetailResponse>> DeclineMatchInvitation(int matchId, CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _matchService.DeclineMatchInvitation(matchId, cancellationToken));
    }
}