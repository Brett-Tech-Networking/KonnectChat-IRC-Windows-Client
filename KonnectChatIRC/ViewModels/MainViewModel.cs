using System.Collections.ObjectModel;
using System.Windows.Input;
using KonnectChatIRC.MVVM;
using KonnectChatIRC.ViewModels;
using KonnectChatIRC.Models;
using KonnectChatIRC.Services;
using System.Collections.Generic;
using System.Linq;

namespace KonnectChatIRC.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private ServerViewModel? _selectedServer;
        private string _connectAddress = "irc.konnectchatirc.net";
        private int _connectPort = 6667;
        private string _connectNick = "";
        private string _autoJoinChannel = "";
        private bool _useServerPassword;
        private string _serverPassword = "";
        private IrcUser? _selectedWhoisUser;
        private bool _isSidebarCollapsed;
        private bool _isUserListCollapsed;
        
        public string ConnectAddress { get => _connectAddress; set => SetProperty(ref _connectAddress, value); }
        public int ConnectPort { get => _connectPort; set => SetProperty(ref _connectPort, value); }
        public string ConnectNick { get => _connectNick; set => SetProperty(ref _connectNick, value); }
        public string AutoJoinChannel { get => _autoJoinChannel; set => SetProperty(ref _autoJoinChannel, value); }
        public bool UseServerPassword { get => _useServerPassword; set => SetProperty(ref _useServerPassword, value); }
        public string ServerPassword { get => _serverPassword; set => SetProperty(ref _serverPassword, value); }

        public IrcUser? SelectedWhoisUser
        {
            get => _selectedWhoisUser;
            set => SetProperty(ref _selectedWhoisUser, value);
        }

        public ObservableCollection<ServerViewModel> Servers { get; } = new ObservableCollection<ServerViewModel>();

        public ServerViewModel? SelectedServer
        {
            get => _selectedServer;
            set => SetProperty(ref _selectedServer, value);
        }

        public bool IsSidebarCollapsed
        {
            get => _isSidebarCollapsed;
            set => SetProperty(ref _isSidebarCollapsed, value);
        }

        public bool IsUserListCollapsed
        {
            get => _isUserListCollapsed;
            set => SetProperty(ref _isUserListCollapsed, value);
        }

        public ICommand ConnectCommand { get; }
        public ICommand AddServerCommand { get; }
        public ICommand RemoveServerCommand { get; }
        public ICommand ToggleSidebarCommand { get; }
        public ICommand ToggleUserListCommand { get; }

        public MainViewModel()
        {
            var rnd = new System.Random();
            ConnectNick = $"KonnectUser{rnd.Next(100, 999)}";
            ConnectCommand = new RelayCommand(ExecuteConnect);
            AddServerCommand = new RelayCommand(_ => SelectedServer = null);
            RemoveServerCommand = new RelayCommand(ExecuteRemoveServer);
            ToggleSidebarCommand = new RelayCommand(_ => IsSidebarCollapsed = !IsSidebarCollapsed);
            ToggleUserListCommand = new RelayCommand(_ => IsUserListCollapsed = !IsUserListCollapsed);
            
            _ = LoadSettingsAsync();
        }

        private List<ServerConfig> _savedConfigs = new List<ServerConfig>();

        private async System.Threading.Tasks.Task LoadSettingsAsync()
        {
            _savedConfigs = await SettingsService.LoadServersAsync();
        }

        public async System.Threading.Tasks.Task SaveSettingsAsync()
        {
            // We want to save both currently active servers AND any previously saved configs
            // that aren't currently active (so we don't lose them just because we aren't connected).
            
            // 1. Create a dictionary of saved configs for easy lookup by address
            var configMap = new Dictionary<string, ServerConfig>();
            foreach (var cfg in _savedConfigs)
            {
                var key = $"{cfg.Address}:{cfg.Port}";
                if (!configMap.ContainsKey(key))
                {
                    configMap[key] = cfg;
                }
            }

            // 2. Update configs from currently active servers
            foreach (var server in Servers)
            {
                var key = $"{server.ServerAddress}:{server.Port}";
                configMap[key] = server.ToConfig();
            }

            // 3. Save everything back to disk and update our in-memory cache
            _savedConfigs = configMap.Values.ToList();
            await SettingsService.SaveServersAsync(_savedConfigs);
        }

        private void ExecuteConnect(object? obj)
        {
            if (obj is ServerViewModel existingServer)
            {
                SelectedServer = existingServer;
                return;
            }

            // Check if we are already connected to this server (Deduplication)
            var duplicate = Servers.FirstOrDefault(s => 
                s.ServerAddress.Equals(ConnectAddress, System.StringComparison.OrdinalIgnoreCase) && 
                s.Port == ConnectPort);

            if (duplicate != null)
            {
                SelectedServer = duplicate;
                return; // Already connected, just switch to it
            }

            var password = UseServerPassword ? ServerPassword : null;
            var serverVm = new ServerViewModel(ConnectAddress, ConnectAddress, ConnectPort, ConnectNick, "Konnect Realname", password, AutoJoinChannel);
            
            // Check if we have a saved config for this server to restore favorites
            var saved = _savedConfigs.FirstOrDefault(c => 
                c.Address.Equals(ConnectAddress, System.StringComparison.OrdinalIgnoreCase) && 
                c.Port == ConnectPort);

            if (saved != null && saved.FavoriteChannels != null)
            {
                foreach (var fav in saved.FavoriteChannels)
                {
                    serverVm.InitialFavoriteChannels.Add(fav);
                }
            }

            serverVm.RequestSave += (s, e) => _ = SaveSettingsAsync();
            Servers.Add(serverVm);
            SelectedServer = serverVm;
            _ = SaveSettingsAsync();
        }

        private void ExecuteRemoveServer(object? obj)
        {
            if (obj is ServerViewModel server)
            {
                // Explicitly disconnect first
                server.Disconnect("KonnectChat IRC Desktop Client - Removed from sidebar");
                
                Servers.Remove(server);
                if (SelectedServer == server)
                {
                    SelectedServer = Servers.Count > 0 ? Servers[0] : null;
                }
                
                // We do NOT remove from _savedConfigs here, effectively "remembering" it for next time
                // The user just said "remove from list", implies closing the active connection.
                // If they want to "forget" settings, that would be a different delete action.
            }
        }
    }
}
