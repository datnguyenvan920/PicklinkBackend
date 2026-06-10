using System;
using System.Collections.Generic;

namespace PicklinkBackend.Models;

public partial class GroupMember
{
    public int GroupId { get; set; }

    public int UserId { get; set; }

    public string Role { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime JoinedAt { get; set; }

    public virtual SocialGroup Group { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
