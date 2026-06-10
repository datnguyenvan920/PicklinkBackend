using System;
using System.Collections.Generic;

namespace PicklinkBackend.Models;

public partial class Friendship
{
    public int FriendshipId { get; set; }

    public int RequesterId { get; set; }

    public int ReceiverId { get; set; }

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual User Receiver { get; set; } = null!;

    public virtual User Requester { get; set; } = null!;
}
