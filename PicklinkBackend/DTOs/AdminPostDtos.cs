using System.ComponentModel.DataAnnotations;

namespace PicklinkBackend.DTOs;

public sealed class AdminPostModerationRequest
{
    public bool IsHidden { get; set; }

    [StringLength(1000)]
    public string? ModerationNote { get; set; }
}

public class AdminPostResponse
{
    public int PostId { get; set; }
    public int AuthorId { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public string? AuthorEmail { get; set; }
    public int? GroupId { get; set; }
    public string? GroupName { get; set; }
    public string? Content { get; set; }
    public string PostType { get; set; } = string.Empty;
    public string Visibility { get; set; } = string.Empty;
    public bool IsHidden { get; set; }
    public string? ModerationNote { get; set; }
    public DateTime? ModeratedAt { get; set; }
    public string? ModeratedByName { get; set; }
    public int LikeCount { get; set; }
    public int CommentCount { get; set; }
    public DateTime CreatedAt { get; set; }
}
