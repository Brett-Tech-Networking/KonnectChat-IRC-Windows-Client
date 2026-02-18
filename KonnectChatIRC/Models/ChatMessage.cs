using System;

namespace KonnectChatIRC.Models
{
    public class ChatMessage
    {
        public required string Sender { get; set; }
        public required string Content { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsIncoming { get; set; }
        public string? Color { get; set; } // For potential nick coloring
        public bool IsSystem { get; set; } // For styling system messages (JOIN, PART, etc.)
        public bool IsAction { get; set; } // For /me actions
        public bool IsRegularMessage => !IsSystem && !IsAction;
        public string SenderPrefix { get; set; } = "";

        public string FormattedTime => Timestamp.ToString(Services.AppSettings.Instance.TimestampFormat);
    }
}
