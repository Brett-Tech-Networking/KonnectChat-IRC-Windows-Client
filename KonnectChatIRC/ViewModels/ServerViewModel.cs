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
            set 
            {
                if (SetProperty(ref _serverName, value))
                {
                    OnPropertyChanged(nameof(ServerInitials));
                }
            }
        }

        public string ServerInitials
        {
            get
            {
                if (string.IsNullOrWhiteSpace(ServerName)) return "?";
                var name = ServerName.ToLower();
                if (name.StartsWith("irc."))
                {
                    var remainder = ServerName.Substring(4);
                    return string.IsNullOrWhiteSpace(remainder) ? "I" : remainder.Substring(0, 1).ToUpper();
                }
                return ServerName.Substring(0, 1).ToUpper();
            }
        }

        private string _currentNick;
        public string CurrentNick
        {
            get => _currentNick;
            set => SetProperty(ref _currentNick, value);
        }

        public ObservableCollection<ChannelViewModel> Channels { get; } = new ObservableCollection<ChannelViewModel>();

        public ChannelViewModel SelectedChannel
        {
            get => _selectedChannel;
            set => SetProperty(ref _selectedChannel, value);
        }

        public ICommand SendCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand ChangeNickCommand { get; }
        public ICommand PartChannelCommand { get; }
        public ICommand ChangeTopicCommand { get; }

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
            _currentNick = nick;

            SendCommand = new RelayCommand(ExecuteSendCommand);
            DisconnectCommand = new RelayCommand(_ => ExecuteDisconnect());
            ChangeNickCommand = new RelayCommand(nick => _ircService?.SendRawAsync($"NICK {nick}"));
            PartChannelCommand = new RelayCommand(chan => _ircService?.SendRawAsync($"PART {chan}"));
            ChangeTopicCommand = new RelayCommand(topic => 
            {
                if (SelectedChannel != null)
                {
                    _ircService?.SendRawAsync($"TOPIC {SelectedChannel.Name} :{topic}");
                }
            });

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
                                Sender = CurrentNick, 
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
                var senderNick = GetNickFromPrefix(e.Prefix);

                // If the target is our own nick, this is a private message to us.
                // The "channel" tab should be named after the sender, not us.
                string channelName = target.Equals(CurrentNick, StringComparison.OrdinalIgnoreCase) 
                    ? senderNick 
                    : target;
                
                var channel = GetOrCreateChannel(channelName);
                if (channel != null)
                {
                    channel.AddMessage(new ChatMessage 
                    { 
                        Sender = senderNick, 
                        Content = content,
                        Timestamp = DateTime.Now,
                        IsIncoming = true
                    });
                }
            }
            else if (e.Command == "NOTICE")
            {
                var target = e.Parameters[0];
                var content = e.Parameters[1];
                var senderNick = GetNickFromPrefix(e.Prefix);

                // Route notices to channel if target is channel, otherwise to Server
                ChannelViewModel? channel = null;
                if (target.StartsWith("#") || target.StartsWith("&"))
                {
                    channel = GetOrCreateChannel(target);
                }
                else if (target.Equals(CurrentNick, StringComparison.OrdinalIgnoreCase))
                {
                    // If it's a personal notice from someone specific, maybe show it in their query tab?
                    // But usually notices are from server or specific users. 
                    // To avoid creating a self-tab, if target == CurrentNick, we route to Server or existing sender tab.
                    if (!string.IsNullOrEmpty(senderNick) && _channelLookup.ContainsKey(senderNick))
                    {
                        channel = _channelLookup[senderNick];
                    }
                    else
                    {
                        channel = _channelLookup["Server"];
                    }
                }
                else
                {
                    channel = _channelLookup["Server"];
                }

                if (channel != null)
                {
                    channel.AddMessage(new ChatMessage 
                    { 
                        Sender = e.Prefix ?? "Notice", 
                        Content = content,
                        Timestamp = DateTime.Now,
                        IsIncoming = true,
                        IsSystem = !target.StartsWith("#") && !target.StartsWith("&") // System style if not a channel notice
                    });
                }
            }
            else if (e.Command == "JOIN")
            {
                if(e.Parameters.Length > 0)
                {
                    var channelName = e.Parameters[0];
                    var channel = GetOrCreateChannel(channelName);
                    if (channel != null)
                    {
                        var nick = GetNickFromPrefix(e.Prefix);
                        if (!nick.Equals(CurrentNick, StringComparison.OrdinalIgnoreCase))
                        {
                            channel.AddUser(new IrcUser(nick));
                        }
                        
                        channel.AddMessage(new ChatMessage 
                        { 
                            Sender = "", 
                            Content = $"* {nick} ({e.Prefix}) has joined {channelName}", 
                            Timestamp = DateTime.Now,
                            IsIncoming = true,
                            IsSystem = true
                        });
                    }
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
                        Content = $"* {nick} left {channelName}", 
                        Timestamp = DateTime.Now,
                        IsIncoming = true,
                        IsSystem = true
                    });
                }
            }
            else if (e.Command == "QUIT")
            {
                var nick = GetNickFromPrefix(e.Prefix);
                var quitMsg = e.Parameters.Length > 0 ? e.Parameters[0] : "Quit";
                foreach(var channel in Channels)
                {
                    if (channel.Users.Any(u => u.Nickname == nick))
                    {
                        channel.RemoveUser(nick);
                        channel.AddMessage(new ChatMessage 
                        { 
                            Sender = "", 
                            Content = $"* {nick} has quit ({quitMsg})", 
                            Timestamp = DateTime.Now,
                            IsIncoming = true,
                            IsSystem = true
                        });
                    }
                }
            }
            else if (e.Command == "KICK")
            {
                var channelName = e.Parameters[0];
                var kickedNick = e.Parameters[1];
                var reason = e.Parameters.Length > 2 ? e.Parameters[2] : "";
                var kicker = GetNickFromPrefix(e.Prefix);
                
                var channel = GetOrCreateChannel(channelName);
                if (channel != null)
                {
                    channel.RemoveUser(kickedNick);
                    channel.AddMessage(new ChatMessage 
                    { 
                        Sender = "", 
                        Content = $"* {kickedNick} was kicked by {kicker} ({reason})", 
                        Timestamp = DateTime.Now,
                        IsIncoming = true,
                        IsSystem = true
                    });
                }
            }
            else if (e.Command == "MODE")
            {
                if (e.Parameters.Length >= 2)
                {
                    var channelName = e.Parameters[0];
                    var mode = e.Parameters[1];
                    var setter = GetNickFromPrefix(e.Prefix);
                    var channel = GetOrCreateChannel(channelName);

                    if (channel != null)
                    {
                        // Handle user mode changes (OP/VOICE)
                        if (e.Parameters.Length >= 3 && (mode.Contains("o") || mode.Contains("v")))
                        {
                            var targetNick = e.Parameters[2];
                            var user = channel.Users.FirstOrDefault(u => u.Nickname.Equals(targetNick, StringComparison.OrdinalIgnoreCase));
                            if (user != null)
                            {
                                // Simple prefix update - in a real client we'd track multiple modes
                                if (mode.Contains("+o")) user.Prefix = "@";
                                else if (mode.Contains("-o")) user.Prefix = "";
                                else if (mode.Contains("+v")) user.Prefix = "+";
                                else if (mode.Contains("-v")) user.Prefix = "";
                            }

                            channel.AddMessage(new ChatMessage 
                            { 
                                Sender = "", 
                                Content = $"* {setter} sets mode {mode} {targetNick}", 
                                Timestamp = DateTime.Now,
                                IsIncoming = true,
                                IsSystem = true
                            });
                        }
                        // Handle channel modes (bans, etc)
                        else if (e.Parameters.Length >= 3)
                        {
                            var mask = e.Parameters[2];
                            channel.AddMessage(new ChatMessage 
                            { 
                                Sender = "", 
                                Content = $"* {setter} sets mode {mode} {mask}", 
                                Timestamp = DateTime.Now,
                                IsIncoming = true,
                                IsSystem = true
                            });
                        }
                    }
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
                        
                         channel.AddMessage(new ChatMessage 
                         { 
                             Sender = "", 
                             Content = $"* {oldNick} is now known as {newNick}", 
                             Timestamp = DateTime.Now,
                             IsIncoming = true,
                             IsSystem = true
                         });
                    }
                }

                if (oldNick.Equals(CurrentNick, StringComparison.OrdinalIgnoreCase))
                {
                    _dispatcherQueue.TryEnqueue(() => CurrentNick = newNick);
                }
            }
            else if (e.Command == "KILL")
            {
                var killedNick = e.Parameters[0];
                var reason = e.Parameters.Length > 1 ? e.Parameters[1] : "";
                
                foreach(var channel in Channels)
                {
                    if (channel.Users.Any(u => u.Nickname == killedNick))
                    {
                        channel.RemoveUser(killedNick);
                        channel.AddMessage(new ChatMessage 
                        { 
                            Sender = "", 
                            Content = $"* {killedNick} was killed ({reason})", 
                            Timestamp = DateTime.Now,
                            IsIncoming = true,
                            IsSystem = true
                        });
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
                // End of list - Sort by user count descending
                _dispatcherQueue.TryEnqueue(() => 
                {
                    var sorted = AvailableChannels.OrderByDescending(c => c.UserCount).ToList();
                    AvailableChannels.Clear();
                    foreach (var c in sorted) AvailableChannels.Add(c);
                });
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
             if (string.IsNullOrEmpty(name)) return _channelLookup["Server"];

             // NEVER create a channel tab for our own nickname
             if (name.Equals(CurrentNick, StringComparison.OrdinalIgnoreCase))
             {
                 return _channelLookup["Server"];
             }

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
                     IsIncoming = true,
                     IsSystem = true
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
                // Strip status prefixes like @, +, %, ~
                char[] statusPrefixes = { '@', '+', '%', '~', '&' };
                while (channelName.Length > 1 && statusPrefixes.Contains(channelName[0]))
                {
                    // But don't strip # if it's the start of the channel name after prefix
                    if (channelName[0] == '&' && channelName.Length > 1 && channelName[1] != '#') break; // Local channels might start with &
                    channelName = channelName.Substring(1);
                }

                // Ensure # prefix if not already present (and not a local & channel)
                if (!channelName.StartsWith("#") && !channelName.StartsWith("&")) channelName = "#" + channelName;

                if (_channelLookup.TryGetValue(channelName, out var existing))
                {
                    _dispatcherQueue.TryEnqueue(() => SelectedChannel = existing);
                }
                else
                {
                    _ircService?.JoinChannelAsync(channelName);
                }
            }
        });

        private void ExecuteDisconnect() 
        {
            Disconnect("KonnectChat IRC Desktop Client");
            
             _dispatcherQueue.TryEnqueue(() => 
             {
                 // Clear channels
                 var serverChan = _channelLookup["Server"];
                 _channelLookup.Clear();
                 _channelLookup.TryAdd("Server", serverChan);
                 
                 Channels.Clear();
                 Channels.Add(serverChan);
                 SelectedChannel = serverChan;
             });
        }

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
