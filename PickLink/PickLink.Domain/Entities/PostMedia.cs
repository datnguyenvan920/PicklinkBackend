using System;
using System.Collections.Generic;
using System.Text;

namespace PickLink.Domain.Entities;

public class PostMedia
{
    public int MediaId { get; set; }
    public int PostId { get; set; }
    public string MediaUrl { get; set; } = string.Empty;
    public string MediaType { get; set; } = "Image";
    public int DisplayOrder { get; set; } = 0;

    public Post Post { get; set; } = null!;
}