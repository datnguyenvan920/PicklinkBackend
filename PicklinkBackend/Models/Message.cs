using System;
using System.Collections.Generic;

namespace PicklinkBackend.Models;

public partial class Message
{
    public int MessageId { get; set; }

    public int ConversationId { get; set; }

    public int SenderId { get; set; }

    public string? Content { get; set; }

    public string MessageType { get; set; } = null!;

    public string? MediaUrl { get; set; }

    public int? ReplyToMessageId { get; set; }

    public DateTime SentAt { get; set; }

    public bool IsDeleted { get; set; }

    public virtual Conversation Conversation { get; set; } = null!;

    public virtual ICollection<Message> InverseReplyToMessage { get; set; } = new List<Message>();

    public virtual Message? ReplyToMessage { get; set; }

    public virtual User Sender { get; set; } = null!;
}
