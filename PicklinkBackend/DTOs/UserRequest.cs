namespace PicklinkBackend.DTOs
{
    public class UserRequest
    {

    }

    public class PlayerSkillRegisterRequest
    {
        public string PreferredPosition { get; set; } = string.Empty;
        public int SkillLevel { get; set; }
    }
}
