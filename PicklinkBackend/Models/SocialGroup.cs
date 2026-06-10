using System;
using System.Collections.Generic;

namespace PicklinkBackend.Models;

public partial class SocialGroup
{
    public int GroupId { get; set; }

    public int OwnerId { get; set; }

    public string GroupName { get; set; } = null!;

    public string? Description { get; set; }

    public string GroupType { get; set; } = null!;

    public string? CoverImageUrl { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<GroupMember> GroupMembers { get; set; } = new List<GroupMember>();

    public virtual Player Owner { get; set; } = null!;
}
