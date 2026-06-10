using System;
using System.Collections.Generic;

namespace PicklinkBackend.Models;

public partial class PostMedia
{
    public int MediaId { get; set; }

    public int PostId { get; set; }

    public string MediaUrl { get; set; } = null!;

    public string MediaType { get; set; } = null!;

    public int DisplayOrder { get; set; }

    public virtual Post Post { get; set; } = null!;
}
