using System;
using System.Collections.Generic;

namespace PicklinkBackend.Models;

public partial class Post
{
    public int PostId { get; set; }

    public int AuthorId { get; set; }

    public string? Content { get; set; }

    public string PostType { get; set; } = null!;

    public string Visibility { get; set; } = null!;

    public DateTime? ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual User Author { get; set; } = null!;

    public virtual ICollection<PostComment> PostComments { get; set; } = new List<PostComment>();

    public virtual ICollection<PostLike> PostLikes { get; set; } = new List<PostLike>();

    public virtual ICollection<PostMedia> PostMedia { get; set; } = new List<PostMedia>();
}
