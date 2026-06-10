using PicklinkMatching.DTO;

namespace PicklinkMatching.Services
{
    public interface IMatchmakingService
    {
        /// <summary>
        /// Adds a lobby to the matchmaking queue. Immediately attempts to find a compatible
        /// partner. Returns the <see cref="Guid"/> that the caller can use to poll for a result.
        /// </summary>
        Task<Guid> EnqueueAsync(Lobby lobby);

        /// <summary>
        /// Returns the fully-matched <see cref="Lobby"/> for the given queue ID, or
        /// <c>null</c> if the lobby is still waiting for a partner.
        /// </summary>
        Task<Lobby?> GetMatchAsync(Guid queueId);
    }
}
