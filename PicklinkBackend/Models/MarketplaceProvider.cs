using System;
using System.Collections.Generic;

namespace PicklinkBackend.Models;

public partial class MarketplaceProvider
{
    public int ProviderId { get; set; }

    public int UserId { get; set; }

    public string? Specialty { get; set; }

    public string? ProviderType { get; set; }

    public virtual ICollection<InventoryItem> InventoryItems { get; set; } = new List<InventoryItem>();

    public virtual User User { get; set; } = null!;
}
