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
                if (Prefix.Contains("~") || Prefix.Contains("&") || Prefix.Contains("@")) return 0; // Admin/Op
                if (Prefix.Contains("%")) return 1; // HalfOp
                if (Prefix.Contains("+")) return 2; // Voice
                return 3; // Normal
            }
        }

        public string RankName
        {
             get
             {
                 if (Rank == 0) return "Operators";
                 if (Rank == 1) return "Half-Ops";
                 if (Rank == 2) return "Voice";
                 return "Users";
             }
        }
        
        public bool IsOp => Prefix.Contains("@") || Prefix.Contains("&") || Prefix.Contains("~");

        public bool IsVoice => Prefix.Contains("+");

        public string FullDisplayName => $"{Prefix}{Nickname}";

        private bool _isOnline = true;
        public bool IsOnline
        {
            get => _isOnline;
            set { _isOnline = value; OnPropertyChanged(); }
        }

        private string _hostname = "";
        private string _username = "";
        private string _realname = "";
        private string _server = "";
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
