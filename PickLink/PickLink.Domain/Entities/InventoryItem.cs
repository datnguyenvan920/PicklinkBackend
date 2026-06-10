using System;
using System.Collections.Generic;
using System.Text;

namespace PickLink.Domain.Entities;

public class InventoryItem
{
    public int ItemId { get; set; }
    public int ProviderId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public decimal PricePerUnit { get; set; } = 0.0m;
    public string Status { get; set; } = "Available";

    public MarketplaceProvider Provider { get; set; } = null!;
}
