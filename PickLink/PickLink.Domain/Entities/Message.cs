using System;
using System.Collections.Generic;
using System.Text;

namespace PickLink.Domain.Entities;

public class Message
{
    public int MessageId { get; set; }
    public int ConversationId { get; set; }
    public int SenderId { get; set; }
    public string? Content { get; set; }
    public string MessageType { get; set; } = "Text"; // Text | Image | System
    public string? MediaUrl { get; set; }
    public int? ReplyToMessageId { get; set; }
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; } = false;

    public Conversation Conversation { get; set; } = null!;
    public User Sender { get; set; } = null!;
    public Message? ReplyToMessage { get; set; }
}
