using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;

namespace KonnectChatIRC.Models
{
    public class IrcUser : INotifyPropertyChanged
    {
        private string _nickname;
        private string _prefix = "";

        public string Nickname 
        { 
            get => _nickname; 
            set { _nickname = value; OnPropertyChanged(); OnPropertyChanged(nameof(FullDisplayName)); }
        }

        public string Prefix 
        { 
            get => _prefix; 
            set 
            { 
                _prefix = value; 
                OnPropertyChanged(); 
                OnPropertyChanged(nameof(IsOp));
                OnPropertyChanged(nameof(IsVoice));
                OnPropertyChanged(nameof(FullDisplayName));
                OnPropertyChanged(nameof(Rank));
            }
        }
        
        public int Rank
        {
            get
            {
                if (Prefix.Contains("~")) return 0; // Owner
                if (Prefix.Contains("&")) return 1; // Admin
                if (Prefix.Contains("@")) return 2; // Operator
                if (Prefix.Contains("%")) return 3; // HalfOp
                if (Prefix.Contains("+")) return 4; // Voice
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
        
        public bool IsOp => Prefix.Contains("@") || Prefix.Contains("&") || Prefix.Contains("~");

        public bool IsVoice => Prefix.Contains("+");

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
