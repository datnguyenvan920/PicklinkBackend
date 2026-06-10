using PicklinkMatching.DTO;
using PicklinkMatching.Services;
using Microsoft.AspNetCore.Mvc;

namespace PicklinkMatching.Controllers
{
    /// <summary>
    /// Matchmaking API.
    ///
    /// Workflow:
    ///   1. POST /api/match/enqueue  → submit a lobby, receive a queueId.
    ///   2. GET  /api/match/{queueId} → poll until the match is ready.
    ///      • 202 Accepted  – still waiting for a partner.
    ///      • 200 OK        – matched! Body contains the full merged Lobby.
    ///      • 404 Not Found – unknown queueId.
    /// </summary>
    [ApiController]
    [Route("api/match")]
    public class ReceiveLobbyController : ControllerBase
    {
        private readonly IMatchmakingService _matchmaking;
        private readonly ILogger<ReceiveLobbyController> _logger;

        public ReceiveLobbyController(IMatchmakingService matchmaking, ILogger<ReceiveLobbyController> logger)
        {
            _matchmaking = matchmaking;
            _logger      = logger;
        }

        // ── POST /api/match/enqueue ──────────────────────────────────────────

        /// <summary>
        /// Submit a lobby to the matchmaking queue.
        /// The caller only provides Players, LobbyType, and LobbySize.
        /// AverageSkill, PreferredTimeStart, PreferredTimeEnd are computed automatically.
        /// </summary>
        /// <param name="request">Lobby submission payload.</param>
        /// <returns>202 Accepted with the assigned queueId.</returns>
        [HttpPost("enqueue")]
        [ProducesResponseType(typeof(EnqueueResponse), StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Enqueue([FromBody] LobbyRequest request)
        {
            _logger.LogInformation("[API] POST /api/match/enqueue  – {PlayerCount} player(s), Type={Type}, Size={Size}",
                request.Players?.Count ?? 0, request.LobbyType, request.LobbySize);

            if (request.Players == null || request.Players.Count == 0)
                return BadRequest("At least one player is required.");

            if (request.LobbySize != 2 && request.LobbySize != 4)
                return BadRequest("LobbySize must be 2 or 4.");

            if (request.Players.Count > request.LobbySize / 2)
                return BadRequest($"A lobby submitted for a {request.LobbySize}-player match can have at most {request.LobbySize / 2} player(s).");

            // Build the internal Lobby from the request (computed fields are derived from Players).
            var lobby = new Lobby
            {
                LobbyType = request.LobbyType,
                LobbySize = request.LobbySize,
                Players   = request.Players
            };

            var queueId = await _matchmaking.EnqueueAsync(lobby);

            _logger.LogInformation("[API] Lobby accepted → QueueId={QueueId}", queueId);

            return Accepted(new EnqueueResponse
            {
                QueueId = queueId,
                Message = "Lobby queued. Poll GET /api/match/{queueId} to check for a match."
            });
        }

        // ── GET /api/match/{queueId} ─────────────────────────────────────────

        /// <summary>
        /// Poll for a match result.
        /// </summary>
        /// <param name="queueId">The queueId returned by the enqueue endpoint.</param>
        /// <returns>
        ///   200 OK   – matched lobby (full player list, computed fields populated).
        ///   202 Accepted – still waiting.
        ///   404      – unknown queueId.
        /// </returns>
        [HttpGet("{queueId:guid}")]
        [ProducesResponseType(typeof(Lobby), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetMatch(Guid queueId)
        {
            _logger.LogInformation("[API] GET /api/match/{QueueId}", queueId);

            var result = await _matchmaking.GetMatchAsync(queueId);

            if (result is not null)
            {
                _logger.LogInformation("[API] Match ready for {QueueId} → returning merged lobby ({PlayerCount} players)",
                    queueId, result.Players.Count);
                return Ok(result);
            }

            _logger.LogInformation("[API] {QueueId} → still waiting for a partner.", queueId);
            return Accepted(new { QueueId = queueId, Status = "Waiting" });
        }
    }

    // ── Response shapes ──────────────────────────────────────────────────────

    public class EnqueueResponse
    {
        public Guid   QueueId { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
