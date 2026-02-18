using System;
using System.Collections.Generic;

namespace KonnectChatIRC.Models
{
    public class WhoisInfo
    {
        public string Nickname { get; set; } = "";
        public string Username { get; set; } = "";
        public string Hostname { get; set; } = "";
        public string Realname { get; set; } = "";
        public string Server { get; set; } = "";
        public string ServerInfo { get; set; } = "";
        public List<string> Channels { get; set; } = new List<string>();
        public bool IsOperator { get; set; }
        public bool IsNetworkAdmin { get; set; }
        public bool IsAway { get; set; }
        public string AwayMessage { get; set; } = "";
        public string ConnectingFrom { get; set; } = "";

        // Idle & connect time from RPL_WHOISIDLE (317)
        public int IdleSeconds { get; set; }
        public DateTime? SignonTime { get; set; }

        public string IdleDisplay
        {
            get
            {
                if (IdleSeconds <= 0) return "Active";
                var ts = TimeSpan.FromSeconds(IdleSeconds);
                if (ts.TotalDays >= 1) return $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m";
                if (ts.TotalHours >= 1) return $"{ts.Hours}h {ts.Minutes}m {ts.Seconds}s";
                if (ts.TotalMinutes >= 1) return $"{ts.Minutes}m {ts.Seconds}s";
                return $"{ts.Seconds}s";
            }
        }

        public string SignonDisplay => SignonTime?.ToLocalTime().ToString("MMM d, yyyy h:mm tt") ?? "Unknown";
    }
}
