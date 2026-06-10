using System;
using System.Collections.Generic;
using System.Text;

namespace PickLink.Domain.Entities;

public class MarketplaceProvider
{
    public int ProviderId { get; set; }
    public int UserId { get; set; }
    public string? Specialty { get; set; }
    public string? ProviderType { get; set; } // Coach | Referee | EquipmentRetailer

    public User User { get; set; } = null!;
    public ICollection<InventoryItem> InventoryItems { get; set; } = new List<InventoryItem>();
}
