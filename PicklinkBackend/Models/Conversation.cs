using System;
using System.Collections.Generic;

namespace PicklinkBackend.Models;

public partial class Conversation
{
    public int ConversationId { get; set; }

    public int? GroupId { get; set; }

    public int? MatchId { get; set; }

    public string ConversationType { get; set; } = null!;

    public string? ConversationName { get; set; }

    public DateTime? LastMessageAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<ConversationParticipant> ConversationParticipants { get; set; } = new List<ConversationParticipant>();

    public virtual SocialGroup? Group { get; set; }

    public virtual Match? Match { get; set; }

    public int? MatchmakingQueueId { get; set; }

    public virtual MatchmakingQueue? MatchmakingQueue { get; set; }

    public virtual ICollection<Message> Messages { get; set; } = new List<Message>();
}
