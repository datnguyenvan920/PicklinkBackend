using System;
using System.Collections.Generic;
using System.Text;

namespace PickLink.Domain.Entities;

public class PostReport
{
    public int ReportId { get; set; }
    public int PostId { get; set; }
    public int ReporterId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Post Post { get; set; } = null!;
    public User Reporter { get; set; } = null!;
}
