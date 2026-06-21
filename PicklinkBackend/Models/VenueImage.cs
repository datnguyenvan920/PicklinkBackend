namespace PicklinkBackend.Models;

public class VenueImage
{
    public int VenueImageId { get; set; }
    public int VenueId { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string? Caption { get; set; }
    public bool IsPrimary { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Venue Venue { get; set; } = null!;
}
