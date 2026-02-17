namespace KonnectChatIRC.Models
{
    public class IrcChannelInfo
    {
        public string Name { get; set; } = "";
        public int UserCount { get; set; }
        public string Topic { get; set; } = "";
        public bool IsFavorite { get; set; }
    }
}
