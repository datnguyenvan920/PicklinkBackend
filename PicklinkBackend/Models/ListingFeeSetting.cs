namespace PicklinkBackend.Models;

public partial class ListingFeeSetting
{
    public int ListingFeeSettingId { get; set; }

    public decimal PricePerCourtPerMonth { get; set; }

    public DateTime UpdatedAt { get; set; }

    public int? UpdatedByUserId { get; set; }

    public virtual User? UpdatedByUser { get; set; }
}
