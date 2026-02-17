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

        private async System.Threading.Tasks.Task LoadSettingsAsync()
        {
            var configs = await SettingsService.LoadServersAsync();
            foreach (var config in configs)
            {
                var vm = new ServerViewModel(config.ServerName, config.Address, config.Port, config.Nick, config.Realname, config.Password, config.AutoJoinChannel);
                foreach (var fav in config.FavoriteChannels)
                {
                    vm.InitialFavoriteChannels.Add(fav);
                }
                vm.RequestSave += (s, e) => _ = SaveSettingsAsync();
                Servers.Add(vm);
            }
        }

        public async System.Threading.Tasks.Task SaveSettingsAsync()
        {
            var configs = Servers.Select(s => s.ToConfig()).ToList();
            await SettingsService.SaveServersAsync(configs);
        }

        private void ExecuteConnect(object? obj)
        {
            if (obj is ServerViewModel existingServer)
            {
                SelectedServer = existingServer;
                return;
            }

            var password = UseServerPassword ? ServerPassword : null;
            var serverVm = new ServerViewModel(ConnectAddress, ConnectAddress, ConnectPort, ConnectNick, "Konnect Realname", password, AutoJoinChannel);
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
                _ = SaveSettingsAsync();
            }
        }
    }
}
