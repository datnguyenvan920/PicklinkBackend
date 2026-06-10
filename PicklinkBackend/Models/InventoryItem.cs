using System;
using System.Collections.Generic;

namespace PicklinkBackend.Models;

public partial class InventoryItem
{
    public int ItemId { get; set; }

    public int ProviderId { get; set; }

    public string ItemName { get; set; } = null!;

    public double PricePerUnit { get; set; }

    public string Status { get; set; } = null!;

    public virtual MarketplaceProvider Provider { get; set; } = null!;
}
