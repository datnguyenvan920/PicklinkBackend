using System;
using System.Collections.Generic;
using System.Text;

namespace PickLink.Domain.Entities;

public class ConversationParticipant
{
    public int ConversationId { get; set; }
    public int UserId { get; set; }
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastReadAt { get; set; }

    public Conversation Conversation { get; set; } = null!;
    public User User { get; set; } = null!;
}