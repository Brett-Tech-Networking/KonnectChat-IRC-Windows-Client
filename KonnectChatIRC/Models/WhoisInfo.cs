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
        public bool IsAway { get; set; }
        public string AwayMessage { get; set; } = "";
    }
}
