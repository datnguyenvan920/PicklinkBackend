using System;
using System.Collections.Generic;
using System.Text;

namespace PickLink.Domain.Entities;

public class PostLike
{
    public int LikeId { get; set; }
    public int PostId { get; set; }
    public int UserId { get; set; }
    public string ReactionType { get; set; } = "Like";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Post Post { get; set; } = null!;
    public User User { get; set; } = null!;
}
