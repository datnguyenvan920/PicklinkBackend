namespace PicklinkMatching.DTO
{
    public class Players
    {
        public int PlayerId { get; set; }
        public string PlayerName { get; set; }
        public float PlayerSkill { get; set; }
        public string PlayerProfilePictureUrl { get; set; }
        public TimeOnly PreferredTimeStart { get; set; }
        public TimeOnly PreferredTimeEnd { get; set; }
        public List<int> PrefferedVenue { get; set; }
    }
}
