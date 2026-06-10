using System;
using System.Collections.Generic;
using System.Text;

namespace PickLink.Domain.Entities;

public class PostComment
{
    public int CommentId { get; set; }
    public int PostId { get; set; }
    public int UserId { get; set; }
    public int? ParentCommentId { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Post Post { get; set; } = null!;
    public User User { get; set; } = null!;
    public PostComment? ParentComment { get; set; }
    public ICollection<PostComment> Replies { get; set; } = new List<PostComment>();
}
