using System;
using System.Collections.Generic;
using System.Text;

namespace PickLink.Domain.Entities;

public class SocialGroup
{
    public int GroupId { get; set; }
    public int OwnerId { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string GroupType { get; set; } = "Public"; // Public | Private | Moderated
    public string? CoverImageUrl { get; set; }
    public bool IsArchived { get; set; } = false;  // BR-07: archive when members < 2
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Player Owner { get; set; } = null!;
    public ICollection<GroupMember> Members { get; set; } = new List<GroupMember>();
}
