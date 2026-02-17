using System.Collections.Generic;

namespace KonnectChatIRC.Models
{
    public class ServerConfig
    {
        public string ServerName { get; set; } = "";
        public string Address { get; set; } = "";
        public int Port { get; set; } = 6667;
        public string Nick { get; set; } = "";
        public string Realname { get; set; } = "Konnect Realname";
        public string? Password { get; set; }
        public string? AutoJoinChannel { get; set; }
        public List<string> FavoriteChannels { get; set; } = new List<string>();
    }
}
