namespace PicklinkMatching.DTO
{
    /// <summary>
    /// The body a client sends when submitting a lobby to the matchmaking queue.
    /// AverageSkill, PreferredTimeStart, and PreferredTimeEnd are NOT provided by the caller –
    /// they are automatically computed from the Players list on the server side.
    /// </summary>
    public class LobbyRequest
    {
        /// <summary>List of players already in this lobby.</summary>
        public List<Players> Players { get; set; } = new();

        /// <summary>"normal" or "ranked"</summary>
        public string LobbyType { get; set; } = string.Empty;

        /// <summary>Total player count for the final match: 2 or 4.</summary>
        public int LobbySize { get; set; }
    }
}
