using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using KonnectChatIRC.Models;
using KonnectChatIRC.Services;
using KonnectChatIRC.MVVM;
using Microsoft.UI.Dispatching;

namespace KonnectChatIRC.ViewModels
{
    public class ServerViewModel : ViewModelBase
    {
        private IrcClientService? _ircService;
        private string _serverName;
        private ChannelViewModel _selectedChannel;
        private readonly DispatcherQueue _dispatcherQueue;
        
        // Thread-safe lookup
        private ConcurrentDictionary<string, ChannelViewModel> _channelLookup = new ConcurrentDictionary<string, ChannelViewModel>(StringComparer.OrdinalIgnoreCase);

        public string ServerName
        {
            get => _serverName;
            set => SetProperty(ref _serverName, value);
        }

        public ObservableCollection<ChannelViewModel> Channels { get; } = new ObservableCollection<ChannelViewModel>();

        public ChannelViewModel SelectedChannel
        {
            get => _selectedChannel;
            set => SetProperty(ref _selectedChannel, value);
        }

        public ICommand SendCommand { get; }

        public ServerViewModel(string serverName, string address, int port, string nick, string realname, string? password = null, string? autoJoinChannel = null)
        {
            _serverName = serverName;
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _ircService = new IrcClientService();
            
            _ircService.MessageReceived += OnMessageReceived;
            
            _ircService.WelcomeReceived += (s, e) => 
            {
                if (!string.IsNullOrEmpty(autoJoinChannel))
                {
                    var channelToJoin = autoJoinChannel.StartsWith("#") ? autoJoinChannel : "#" + autoJoinChannel;
                    _ = _ircService.JoinChannelAsync(channelToJoin); // Fire and forget in event handler
                }
            };

            _ircService.ChannelFound += (s, info) => 
            {
                AvailableChannels.Add(info);
            };

            _ircService.WhoisReceived += (s, info) => 
            {
                _dispatcherQueue.TryEnqueue(() => 
                {
                    foreach (var channel in Channels)
                    {
                        var user = channel.Users.FirstOrDefault(u => u.Nickname.Equals(info.Nickname, StringComparison.OrdinalIgnoreCase));
                        if (user != null)
                        {
                            user.Hostname = info.Hostname;
                            user.Username = info.Username;
                            user.Realname = info.Realname;
                            user.Server = info.Server;
                            user.Channels.Clear();
                            foreach(var c in info.Channels) user.Channels.Add(c);
                        }
                    }
                });
            };

            _ircService.Disconnected += (s, e) => AddSystemMessage("Disconnected from server.");
            _ircService.ErrorOccurred += (s, msg) => AddSystemMessage($"Error: {msg}");

            // Default "Server" channel
            var serverChan = new ChannelViewModel("Server");
            _channelLookup.TryAdd("Server", serverChan);
            Channels.Add(serverChan);
            _selectedChannel = serverChan; // Initialize backing field

            SendCommand = new RelayCommand(ExecuteSendCommand);

            // Start connection
            HandleConnection(address, port, nick, realname, password);
        }

        private async void HandleConnection(string address, int port, string nick, string realname, string? password)
        {
            if (_ircService != null)
            {
                await _ircService.ConnectAsync(address, port, nick, realname, password);
            }
        }

        private void ExecuteSendCommand(object? parameter)
        {
            if (parameter is string text && !string.IsNullOrWhiteSpace(text))
            {
                if (SelectedChannel != null)
                {
                    if (text.StartsWith("/"))
                    {
                        var parts = text.Split(' ');
                        var cmd = parts[0].ToLower();
                        if (cmd == "/join" && parts.Length > 1)
                        {
                            _ircService?.JoinChannelAsync(parts[1]);
                        }
                        else if (cmd == "/quit")
                        {
                            string msg = parts.Length > 1 ? text.Substring(6) : "KonnectChat IRC Desktop Client";
                            Disconnect(msg);
                        }
                        else
                        {
                            _ircService?.SendRawAsync(text.Substring(1));
                        }
                    }
                    else
                    {
                        if (SelectedChannel.Name != "Server")
                        {
                            _ircService?.SendMessageAsync(SelectedChannel.Name, text);
                            SelectedChannel.AddMessage(new ChatMessage 
                            { 
                                Sender = "Me", 
                                Content = text, 
                                Timestamp = DateTime.Now, 
                                IsIncoming = false 
                            });
                        }
                    }
                }
            }
        }

        public void Disconnect(string quitMessage)
        {
            _ircService?.Disconnect(quitMessage);
        }

        private void OnMessageReceived(object? sender, IrcMessageEventArgs e)
        {
            if (e.Command == "PRIVMSG")
            {
                var target = e.Parameters[0];
                var content = e.Parameters[1];
                
                var channel = GetOrCreateChannel(target);
                if (channel != null)
                {
                    channel.AddMessage(new ChatMessage 
                    { 
                        Sender = e.Prefix, 
                        Content = content,
                        Timestamp = DateTime.Now,
                        IsIncoming = true
                    });
                }
            }
            else if (e.Command == "JOIN")
            {
                if(e.Parameters.Length > 0)
                {
                     GetOrCreateChannel(e.Parameters[0]);
                }
            }
            else if (e.Command == "PART")
            {
                var channelName = e.Parameters[0];
                var channel = GetOrCreateChannel(channelName);
                if (channel != null)
                {
                    var nick = GetNickFromPrefix(e.Prefix);
                    channel.RemoveUser(nick);
                    channel.AddMessage(new ChatMessage 
                    { 
                        Sender = "", 
                        Content = $"{nick} left {channelName}", 
                        Timestamp = DateTime.Now,
                        IsIncoming = true 
                    });
                }
            }
            else if (e.Command == "QUIT")
            {
                var nick = GetNickFromPrefix(e.Prefix);
                foreach(var channel in Channels)
                {
                    channel.RemoveUser(nick);
                }
            }
            else if (e.Command == "NICK")
            {
                var oldNick = GetNickFromPrefix(e.Prefix);
                var newNick = e.Parameters[0];
                if (e.Parameters[0].StartsWith(":")) newNick = newNick.Substring(1);

                foreach(var channel in Channels)
                {
                    var user = channel.Users.FirstOrDefault(u => u.Nickname == oldNick);
                    if (user != null)
                    {
                         channel.RemoveUser(oldNick);
                         user.Nickname = newNick;
                         channel.AddUser(user);
                    }
                }
            }
            else if (e.Command == "353") 
            {
                if (e.Parameters.Length >= 4)
                {
                    var channelName = e.Parameters[2];
                    var nicksParam = e.Parameters[3];
                    var channel = GetOrCreateChannel(channelName);
                    
                    if (channel != null)
                    {
                        var nicks = nicksParam.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        foreach (var nick in nicks)
                        {
                            string cleanNick = nick;
                            string prefix = "";
                            if (nick.StartsWith("@") || nick.StartsWith("+") || nick.StartsWith("%"))
                            {
                                prefix = nick.Substring(0, 1);
                                cleanNick = nick.Substring(1);
                            }
                            if (!channel.Users.Any(u => u.Nickname == cleanNick))
                            {
                                channel.AddUser(new IrcUser(cleanNick) { Prefix = prefix });
                            }
                        }
                    }
                }
            }
            else if (e.Command == "332")
            {
                if (e.Parameters.Length >= 3)
                {
                    var channelName = e.Parameters[1];
                    var topic = e.Parameters[2];
                    var channel = GetOrCreateChannel(channelName);
                    if (channel != null)
                    {
                        _dispatcherQueue.TryEnqueue(() => channel.Topic = topic);
                    }
                }
            }
            else if (e.Command == "322") // RPL_LIST
            {
                // 322 Nick #channel 5 :topic
                if (e.Parameters.Length >= 3)
                {
                    var chanName = e.Parameters[1];
                    var userCountStr = e.Parameters[2];
                    var topic = e.Parameters.Length > 3 ? e.Parameters[3] : "";
                    
                    if (int.TryParse(userCountStr, out int count))
                    {
                        _dispatcherQueue.TryEnqueue(() => 
                        {
                            AvailableChannels.Add(new IrcChannelInfo { Name = chanName, UserCount = count, Topic = topic });
                        });
                    }
                }
                return; // Don't show in chat log
            }
            else if (e.Command == "323") // RPL_LISTEND
            {
                // End of list
                return; // Don't show in chat log
            }
            
            Channels[0].AddMessage(new ChatMessage 
            { 
                Sender = "", 
                Content = e.RawMessage, 
                Timestamp = DateTime.Now,
                IsIncoming = true
            });
        }

        private string GetNickFromPrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return "";
            var idx = prefix.IndexOf('!');
            return idx != -1 ? prefix.Substring(0, idx) : prefix;
        }

        private ChannelViewModel? GetOrCreateChannel(string name)
        {
             if (_channelLookup.TryGetValue(name, out var existing))
             {
                 return existing;
             }

             var newChan = new ChannelViewModel(name);
             if (_channelLookup.TryAdd(name, newChan))
             {
                 _dispatcherQueue.TryEnqueue(() => Channels.Add(newChan));
                 return newChan;
             }
             
             _channelLookup.TryGetValue(name, out var winner);
             return winner;
        }

        private void AddSystemMessage(string msg)
        {
             _dispatcherQueue.TryEnqueue(() => 
             {
                 Channels[0].AddMessage(new ChatMessage 
                 { 
                     Sender = "System", 
                     Content = msg, 
                     Timestamp = DateTime.Now,
                     IsIncoming = true
                 });
             });
        }
        
        public ObservableCollection<IrcChannelInfo> AvailableChannels { get; } = new ObservableCollection<IrcChannelInfo>();
        
        public ICommand RefreshChannelListCommand => new RelayCommand(async _ => 
        {
            AvailableChannels.Clear();
            await _ircService!.ListChannelsAsync();
        });

        public ICommand JoinChannelCommand => new RelayCommand(param => 
        {
            if (param is string channelName && !string.IsNullOrWhiteSpace(channelName))
            {
                // Ensure # prefix
                if (!channelName.StartsWith("#")) channelName = "#" + channelName;
                 _ircService?.JoinChannelAsync(channelName);
            }
        });

        public ICommand PartChannelCommand => new RelayCommand(_ => 
        {
            if (SelectedChannel != null && SelectedChannel.Name != "Server")
            {
                var chanName = SelectedChannel.Name;
                _ircService?.SendRawAsync($"PART {chanName}");
                
                // Remove from list
                if (_channelLookup.TryRemove(chanName, out var removedChan))
                {
                    _dispatcherQueue.TryEnqueue(() => 
                    {
                        Channels.Remove(removedChan);
                        SelectedChannel = Channels.FirstOrDefault(c => c.Name == "Server") ?? Channels.First(); // Go back to Server or first avail
                    });
                }
            }
        });

        public ICommand DisconnectCommand => new RelayCommand(_ => 
        {
            Disconnect("KonnectChat IRC Desktop Client");
            // Navigate back to Server tab
             _dispatcherQueue.TryEnqueue(() => 
             {
                 SelectedChannel = Channels.FirstOrDefault(c => c.Name == "Server") ?? Channels.First();
             });
        });

        public ICommand KickUserCommand => new RelayCommand(param => 
        {
             if (param is IrcUser user && SelectedChannel != null)
             {
                 _ = _ircService?.SendRawAsync($"KICK {SelectedChannel.Name} {user.Nickname} :Kicked by admin");
             }
        });

        public ICommand BanUserCommand => new RelayCommand(param => 
        {
             if (param is IrcUser user && SelectedChannel != null)
             {
                 _ = _ircService?.SendRawAsync($"MODE {SelectedChannel.Name} +b {user.Nickname}!*@*");
                 _ = _ircService?.SendRawAsync($"KICK {SelectedChannel.Name} {user.Nickname} :Banned by admin");
             }
        });

        public ICommand WhoisUserCommand => new RelayCommand(param => 
        {
             if (param is IrcUser user)
             {
                 _ = _ircService?.SendRawAsync($"WHOIS {user.Nickname}");
             }
        });
        
        // Event for View to subscribe to for showing dialog
        // public event EventHandler<WhoisInfo>? ShowWhoisDialog;
    }
}
