using System;
using System.Collections.Generic;
using System.Text;

namespace PickLink.Domain.Entities;

using PickLink.Domain.Enums;

public class Post
{
    public int PostId { get; set; }
    public int AuthorId { get; set; }
    public string? Content { get; set; }
    public string PostType { get; set; } = "Post"; // Post | Story
    public PostVisibility Visibility { get; set; } = PostVisibility.Public;
    public bool IsHidden { get; set; } = false;    // BR-16: auto-hidden at 5 reports
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User Author { get; set; } = null!;
    public ICollection<PostMedia> MediaItems { get; set; } = new List<PostMedia>();
    public ICollection<PostLike> Likes { get; set; } = new List<PostLike>();
    public ICollection<PostComment> Comments { get; set; } = new List<PostComment>();
    public ICollection<PostReport> Reports { get; set; } = new List<PostReport>();
}
