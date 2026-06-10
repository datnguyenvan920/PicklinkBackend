using System;
using System.Collections.Generic;
using System.Text;

namespace PickLink.Domain.Entities;

using PickLink.Domain.Enums;

public class Friendship
{
    public int FriendshipId { get; set; }
    public int RequesterId { get; set; }
    public int ReceiverId { get; set; }
    public FriendshipStatus Status { get; set; } = FriendshipStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User Requester { get; set; } = null!;
    public User Receiver { get; set; } = null!;
}
