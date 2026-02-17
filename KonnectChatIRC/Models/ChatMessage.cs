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

        public string FormattedTime => Timestamp.ToString("HH:mm:ss");
    }
}
