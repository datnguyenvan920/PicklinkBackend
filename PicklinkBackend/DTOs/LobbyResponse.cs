using PicklinkBackend.Models;

namespace PicklinkBackend.DTOs
{
    /// <summary>
    /// Represents a single player entry inside a <see cref="LobbyResponse"/>.
    /// Renamed from "Players" to avoid a Swashbuckle schema-ID conflict with the
    /// EF model collection of the same name, which caused Swagger to return 500.
    /// The JSON property name on <see cref="LobbyResponse.Players"/> is unchanged.
    /// </summary>
    public class LobbyPlayerDto
    {
        public int PlayerId { get; set; }
        public string PlayerName { get; set; } = string.Empty;
        public float PlayerSkill { get; set; }
        public string? PlayerProfilePictureUrl { get; set; }
        public TimeOnly PreferredTimeStart { get; set; }
        public TimeOnly PreferredTimeEnd { get; set; }
        public List<int> PrefferedVenue { get; set; } = new();
    }

    public class LobbyResponse
    {
        /// <summary>Unique identifier assigned when this lobby enters the queue.</summary>
        public Guid QueueId { get; set; } = Guid.NewGuid();

        /// <summary>Set when the lobby has been matched with a partner lobby.</summary>
        public DateTime? MatchedAt { get; set; }

        public List<LobbyPlayerDto> Players { get; set; } = new();
        public string LobbyType { get; set; } = string.Empty;  // "normal" or "ranked"
        public int LobbySize { get; set; }                      // 2 or 4 (total players in the final match)

        // â”€â”€ Computed properties (auto-derived from Players) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>Average skill level across all players in this lobby.</summary>
        public float AverageSkill
        {
            get
            {
                if (Players == null || Players.Count == 0)
                    return 0;
                return Players.Average(p => p.PlayerSkill);
            }
        }

        /// <summary>
        /// Earliest shared start time: the latest individual PreferredTimeStart,
        /// so every player in the lobby is already available.
        /// </summary>
        public TimeOnly PreferredTimeStart
        {
            get
            {
                if (Players == null || Players.Count == 0)
                    return TimeOnly.MinValue;
                return Players.Max(p => p.PreferredTimeStart);
            }
        }

        /// <summary>
        /// Latest shared end time: the earliest individual PreferredTimeEnd,
        /// so every player in the lobby is still available.
        /// </summary>
        public TimeOnly PreferredTimeEnd
        {
            get
            {
                if (Players == null || Players.Count == 0)
                    return TimeOnly.MaxValue;
                return Players.Min(p => p.PreferredTimeEnd);
            }
        }

        /// <summary>
        /// The set of venue IDs that every player in this lobby is willing to play at.
        /// </summary>
        public IEnumerable<int> SharedVenues
        {
            get
            {
                if (Players == null || Players.Count == 0)
                    return Enumerable.Empty<int>();

                return Players
                    .Select(p => p.PrefferedVenue ?? new List<int>())
                    .Aggregate((a, b) => a.Intersect(b).ToList());
            }
        }
    }
}
