using System;
using System.Collections.Generic;
using System.Text;

namespace PickLink.Domain.Entities;

public class Conversation
{
    public int ConversationId { get; set; }
    public string ConversationType { get; set; } = "Direct"; // Direct | Group | Match
    public string? ConversationName { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ConversationParticipant> Participants { get; set; } = new List<ConversationParticipant>();
    public ICollection<Message> Messages { get; set; } = new List<Message>();
}
