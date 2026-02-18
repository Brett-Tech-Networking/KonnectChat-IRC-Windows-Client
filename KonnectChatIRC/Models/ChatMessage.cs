using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KonnectChatIRC.Models
{
    public class ChatMessage : INotifyPropertyChanged
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

        public string FormattedTime => Timestamp.ToString(Services.AppSettings.TimestampFormat);

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public void NotifyTimeChanged() => OnPropertyChanged(nameof(FormattedTime));
    }
}
