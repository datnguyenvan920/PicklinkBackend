namespace PicklinkBackend.Models;

public class FavoriteVenue
{
    public int PlayerId { get; set; }
    public int VenueId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Player Player { get; set; } = null!;
    public virtual Venue Venue { get; set; } = null!;
}
