using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;

namespace KonnectChatIRC.Models
{
    public class IrcUser : INotifyPropertyChanged
    {
        private string _nickname;
        private HashSet<char> _activePrefixes = new HashSet<char>();
        
        // Helper to determine highest rank prefix for display
        public string HeaderPrefix
        {
            get
            {
                if (_activePrefixes.Contains('~')) return "~";
                if (_activePrefixes.Contains('&')) return "&";
                if (_activePrefixes.Contains('@')) return "@";
                if (_activePrefixes.Contains('%')) return "%";
                if (_activePrefixes.Contains('+')) return "+";
                return "";
            }
        }

        public string Prefix 
        { 
            get => HeaderPrefix;
            set 
            { 
                _activePrefixes.Clear();
                if (!string.IsNullOrEmpty(value))
                {
                    // If setting a raw string (e.g. from 353), it might contain multiple chars if server supports it, or just one
                    foreach(char c in value) _activePrefixes.Add(c);
                }
                NotifyPrefixChanges();
            }
        }
        
        public void AddPrefix(char p)
        {
            if (_activePrefixes.Add(p)) NotifyPrefixChanges();
        }

        public void RemovePrefix(char p)
        {
            if (_activePrefixes.Remove(p)) NotifyPrefixChanges();
        }

        private void NotifyPrefixChanges()
        {
            OnPropertyChanged(nameof(Prefix));
            OnPropertyChanged(nameof(IsOp));
            OnPropertyChanged(nameof(IsVoice));
            OnPropertyChanged(nameof(FullDisplayName));
            OnPropertyChanged(nameof(Rank));
        }

        public int Rank
        {
            get
            {
                if (_activePrefixes.Contains('~')) return 0; // Owner
                if (_activePrefixes.Contains('&')) return 1; // Admin
                if (_activePrefixes.Contains('@')) return 2; // Operator
                if (_activePrefixes.Contains('%')) return 3; // HalfOp
                if (_activePrefixes.Contains('+')) return 4; // Voice
                return 5; // Normal
            }
        }

        public string RankName
        {
             get
             {
                 if (Rank == 0) return "Owners";
                 if (Rank == 1) return "Admins";
                 if (Rank == 2) return "Operators";
                 if (Rank == 3) return "Half-Ops";
                 if (Rank == 4) return "Voice";
                 return "Users";
             }
        }
        
        public bool IsOp => _activePrefixes.Contains('@') || _activePrefixes.Contains('&') || _activePrefixes.Contains('~');

        public bool IsVoice => _activePrefixes.Contains('+');

        public string FullDisplayName => $"{Prefix}{Nickname}";

        private bool _isAway = false;
        public bool IsAway
        {
            get => _isAway;
            set { _isAway = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsOnline)); }
        }

        private string _awayMessage = "";
        public string AwayMessage
        {
            get => _awayMessage;
            set { _awayMessage = value; OnPropertyChanged(); }
        }

        // Computed: user is online when not away
        public bool IsOnline => !IsAway;

        private string _hostname = "";
        private string _username = "";
        private string _realname = "";
        private string _server = "";
        private string _connectingFrom = "";
        private bool _isIrcOp = false;
        private bool _isNetworkAdmin = false;
        private ObservableCollection<string> _channels = new ObservableCollection<string>();

        public string Hostname 
        { 
            get => _hostname; 
            set { _hostname = value; OnPropertyChanged(); }
        }

        public string Username 
        { 
            get => _username; 
            set { _username = value; OnPropertyChanged(); }
        }

        public string Realname 
        { 
            get => _realname; 
            set { _realname = value; OnPropertyChanged(); }
        }

        public string Server 
        { 
            get => _server; 
            set { _server = value; OnPropertyChanged(); }
        }

        public string ConnectingFrom
        {
            get => _connectingFrom;
            set { _connectingFrom = value; OnPropertyChanged(); }
        }

        public bool IsIrcOp
        {
            get => _isIrcOp;
            set { _isIrcOp = value; OnPropertyChanged(); }
        }

        public bool IsNetworkAdmin
        {
            get => _isNetworkAdmin;
            set { _isNetworkAdmin = value; OnPropertyChanged(); }
        }

        public ObservableCollection<string> Channels 
        { 
            get => _channels; 
            set { _channels = value; OnPropertyChanged(); }
        }

        public IrcUser(string nickname)
        {
            _nickname = nickname;
        }

        public IrcUser() 
        { 
            _nickname = "Unknown"; 
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
