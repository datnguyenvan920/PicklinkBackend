using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PicklinkBackend.Startup;
using PicklinkBackend.DTOs;

namespace PicklinkBackend.Controllers;

public partial class MatchController
{
    [Authorize]
    [HttpGet("lobby-me")]
    public async Task<ActionResult<LobbyMeResponse>> LobbyMe()
    {
        SetCurrentUser();
        return ToActionResult(await _matchService.LobbyMe());
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> CreateMatch(CreateMatchRequest createMatch)
    {
        SetCurrentUser();
        return ToActionResult(await _matchService.CreateMatch(createMatch));
    }

    [Authorize]
    [HttpGet("my-matches")]
    public async Task<ActionResult<List<MyMatchResponse>>> MyMatches()
    {
        SetCurrentUser();
        return ToActionResult(await _matchService.MyMatches());
    }

    [Authorize]
    [HttpGet("{matchId:int}/voting-status")]
    public async Task<ActionResult<MatchVotingStatusResponse>> GetVotingStatus(int matchId)
    {
        SetCurrentUser();
        return ToActionResult(await _matchService.GetVotingStatus(matchId));
    }

    [Authorize]
    [HttpPost("{matchId:int}/vote")]
    public async Task<ActionResult<MatchVotingStatusResponse>> Vote(
        int matchId,
        CastVoteRequest request,
        CancellationToken cancellationToken)
    {
        SetCurrentUser();
        return ToActionResult(await _matchService.Vote(matchId, request, cancellationToken));
    }

    [Authorize]
    [HttpGet("{matchId:int}/detail")]
    public async Task<ActionResult<MatchDetailResponse>> GetDetail(int matchId)
    {
        SetCurrentUser();
        return ToActionResult(await _matchService.GetDetail(matchId));
    }

    [Authorize]
    [HttpGet("{matchId:int}/messages")]
    public async Task<IActionResult> GetMessages(int matchId)
    {
        SetCurrentUser();
        return ToActionResult(await _matchService.GetMessages(matchId));
    }

    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Messaging)]
    [HttpPost("{matchId:int}/messages")]
    public async Task<IActionResult> SendMessage(int matchId, SendMatchMessageRequest request)
    {
        SetCurrentUser();
        return ToActionResult(await _matchService.SendMessage(matchId, request));
    }
}