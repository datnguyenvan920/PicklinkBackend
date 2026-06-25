namespace PicklinkBackend.Models;

public partial class GroupImage
{
    public int GroupImageId { get; set; }

    public int GroupId { get; set; }

    public string ImageUrl { get; set; } = null!;

    public string? Caption { get; set; }

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual SocialGroup Group { get; set; } = null!;
}
