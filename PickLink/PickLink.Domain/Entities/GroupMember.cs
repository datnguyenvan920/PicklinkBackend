using System;
using System.Collections.Generic;
using System.Text;

namespace PickLink.Domain.Entities;

public class GroupMember
{
    public int GroupId { get; set; }
    public int UserId { get; set; }
    public string Role { get; set; } = "Member"; // Member | Moderator | Owner
    public string Status { get; set; } = "Accepted"; // Pending | Accepted
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    public SocialGroup Group { get; set; } = null!;
    public User User { get; set; } = null!;
}
