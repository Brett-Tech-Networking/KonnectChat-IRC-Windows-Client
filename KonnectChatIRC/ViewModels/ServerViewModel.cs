using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        private string _address;
        private int _port;
        private string _realname;
        private string? _password;
        private string? _autoJoinChannel;

        public event EventHandler? RequestSave;
        public List<string> InitialFavoriteChannels { get; } = new List<string>();
        
        // Thread-safe lookup
        private ConcurrentDictionary<string, ChannelViewModel> _channelLookup = new ConcurrentDictionary<string, ChannelViewModel>(StringComparer.OrdinalIgnoreCase);


        public string ServerAddress => _address;
        public int Port => _port;

        public string ChannelSearchText
        {
            get => _channelSearchText;
            set
            {
                if (SetProperty(ref _channelSearchText, value))
                {
                    RefreshFilteredAvailableChannels();
                }
            }
        }

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
        public ObservableCollection<ChannelViewModel> FavoriteChannels { get; } = new ObservableCollection<ChannelViewModel>();
        public ObservableCollection<ChannelViewModel> OtherChannels { get; } = new ObservableCollection<ChannelViewModel>();

        public ChannelViewModel SelectedChannel
        {
            get => _selectedChannel;
            set => SetProperty(ref _selectedChannel, value);
        }

        private bool _isIrcOp;
        
        public bool IsIrcOp 
        { 
            get => _isIrcOp; 
            set => SetProperty(ref _isIrcOp, value); 
        }

        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand SendCommand { get; }
        public ICommand RemoveServerCommand { get; }
        public ICommand RefreshChannelListCommand { get; }
        public ICommand JoinChannelCommand { get; }
        public ICommand WhoisUserCommand { get; }
        public ICommand KickUserCommand { get; }
        public ICommand BanUserCommand { get; }
        public ICommand KillUserCommand { get; }
        public ICommand GlineUserCommand { get; }
        public ICommand ChangeNickCommand { get; }
        public ICommand PartChannelCommand { get; }
        public ICommand ChangeTopicCommand { get; }
        public ICommand ToggleFavoriteCommand { get; }

        public event EventHandler<IrcUser>? RequestKillDialog;
        public event EventHandler<IrcUser>? RequestGlineDialog;

        public ServerViewModel(string serverName, string address, int port, string nick, string realname, string? password, string? autoJoinChannel)
        {
            _serverName = serverName;
            _address = address;
            _port = port;
            _currentNick = nick;
            _realname = realname;
            _password = password;
            _autoJoinChannel = autoJoinChannel;

            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _ircService = new IrcClientService();

            ConnectCommand = new RelayCommand(_ => ExecuteConnect());
            DisconnectCommand = new RelayCommand(_ => ExecuteDisconnect());
            SendCommand = new RelayCommand(ExecuteSendCommand);
            RemoveServerCommand = new RelayCommand(_ => ExecuteRemoveServer());
            RefreshChannelListCommand = new RelayCommand(_ => ExecuteRefreshChannelList());
            JoinChannelCommand = new RelayCommand(param => ExecuteJoinChannel(param as string));
            WhoisUserCommand = new RelayCommand(param => ExecuteWhoisUser(param as IrcUser));
            KickUserCommand = new RelayCommand(param => ExecuteKickUser(param as IrcUser));
            BanUserCommand = new RelayCommand(param => ExecuteBanUser(param as IrcUser));
            KillUserCommand = new RelayCommand(param => ExecuteKillUser(param as IrcUser));
            GlineUserCommand = new RelayCommand(param => ExecuteGlineUser(param as IrcUser));
            
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
                lock (_channelListLock)
                {
                    info.IsFavorite = InitialFavoriteChannels.Any(c => c.Equals(info.Name, StringComparison.OrdinalIgnoreCase));
                    _allAvailableChannels.Add(info);
                }
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
                            user.ConnectingFrom = info.ConnectingFrom;
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
            OtherChannels.Add(serverChan);
            _selectedChannel = serverChan; // Initialize backing field
            _currentNick = nick;

            // SendCommand already initialized above
            // DisconnectCommand already initialized above
            ChangeNickCommand = new RelayCommand(nick => _ircService?.SendRawAsync($"NICK {nick}"));
            PartChannelCommand = new RelayCommand(chan => _ircService?.SendRawAsync($"PART {chan}"));
            ChangeTopicCommand = new RelayCommand(topicObj => 
            {
                var topic = topicObj as string;
                if (SelectedChannel != null)
                {
                    _ircService?.SendRawAsync($"TOPIC {SelectedChannel.Name} :{topic}");
                }
            });

            ToggleFavoriteCommand = new RelayCommand(param =>
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    string channelName = "";
                    bool isSetToFavorite = false;

                    if (param is ChannelViewModel channel)
                    {
                        channel.IsFavorite = !channel.IsFavorite;
                        channelName = channel.Name;
                        isSetToFavorite = channel.IsFavorite;

                        if (isSetToFavorite)
                        {
                            if (OtherChannels.Contains(channel)) OtherChannels.Remove(channel);
                            if (!FavoriteChannels.Contains(channel)) FavoriteChannels.Add(channel);
                        }
                        else
                        {
                            if (FavoriteChannels.Contains(channel)) FavoriteChannels.Remove(channel);
                            if (!OtherChannels.Contains(channel)) OtherChannels.Add(channel);
                        }
                    }
                    else if (param is IrcChannelInfo info)
                    {
                        info.IsFavorite = !info.IsFavorite;
                        channelName = info.Name;
                        isSetToFavorite = info.IsFavorite;
                        
                        // If this channel is already joined, sync its ViewModel
                        if (_channelLookup.TryGetValue(channelName, out var joinedChan))
                        {
                            joinedChan.IsFavorite = isSetToFavorite;
                            if (isSetToFavorite)
                            {
                                if (OtherChannels.Contains(joinedChan)) OtherChannels.Remove(joinedChan);
                                if (!FavoriteChannels.Contains(joinedChan)) FavoriteChannels.Add(joinedChan);
                            }
                            else
                            {
                                if (FavoriteChannels.Contains(joinedChan)) FavoriteChannels.Remove(joinedChan);
                                if (!OtherChannels.Contains(joinedChan)) OtherChannels.Add(joinedChan);
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(channelName))
                    {
                        if (isSetToFavorite)
                        {
                            if (!InitialFavoriteChannels.Contains(channelName)) InitialFavoriteChannels.Add(channelName);
                        }
                        else
                        {
                            InitialFavoriteChannels.Remove(channelName);
                        }
                        RequestSave?.Invoke(this, EventArgs.Empty);
                    }
                });
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
                    var senderUser = channel.Users.FirstOrDefault(u => u.Nickname.Equals(senderNick, StringComparison.OrdinalIgnoreCase));
                    string prefix = senderUser?.Prefix ?? "";

                    channel.AddMessage(new ChatMessage 
                    { 
                        Sender = senderNick, 
                        SenderPrefix = prefix,
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
                    // If it's a personal notice, and we have a query window, show it there
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
                        IsSystem = true // Notices should usually be system-width
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
                            var newUser = new IrcUser(nick);
                            
                            // Parse hostname from prefix: nick!user@host
                            if (e.Prefix.Contains("@"))
                            {
                                var parts = e.Prefix.Split('@');
                                if (parts.Length > 1)
                                {
                                    newUser.Hostname = parts[1];
                                }
                            }

                            channel.AddUser(newUser);
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
                var nick = GetNickFromPrefix(e.Prefix);

                if (nick.Equals(CurrentNick, StringComparison.OrdinalIgnoreCase))
                {
                    // If WE are parting, remove the channel entirely
                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        if (_channelLookup.TryRemove(channelName, out var removedChannel))
                        {
                            Channels.Remove(removedChannel);
                            if (FavoriteChannels.Contains(removedChannel)) FavoriteChannels.Remove(removedChannel);
                            if (OtherChannels.Contains(removedChannel)) OtherChannels.Remove(removedChannel);

                            if (SelectedChannel == removedChannel)
                            {
                                SelectedChannel = Channels[0]; // Usually "Server"
                            }
                        }
                    });
                }
                else
                {
                    var channel = GetOrCreateChannel(channelName);
                    if (channel != null)
                    {
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
            else if (e.Command == "381") // RPL_YOUREOPER
            {
                _dispatcherQueue.TryEnqueue(() => IsIrcOp = true);
            }
            else if (e.Command == "MODE")
            {
                if (e.Parameters.Length >= 2)
                {
                    var target = e.Parameters[0];
                    var modeStr = e.Parameters[1];
                    
                    // Check if mode is for us
                    if (target.Equals(CurrentNick, StringComparison.OrdinalIgnoreCase))
                    {
                        // User mode change for self
                        if (modeStr.Contains("+o") || modeStr.Contains("+O") || modeStr.Contains("+a") || modeStr.Contains("+A"))
                        {
                            _dispatcherQueue.TryEnqueue(() => IsIrcOp = true);
                        }
                        // Optionally handle removal, though usually stays once granted until disconnect
                    }
                    else if (target.StartsWith("#") || target.StartsWith("&"))
                    {
                        // Channel mode
                        var channel = GetOrCreateChannel(target);

                        if (channel != null)
                        {
                            // Handle user mode changes (Owner/Admin/OP/HalfOp/Voice)
                            // Modes: q(Owner), a(Admin), o(Op), h(HalfOp), v(Voice)
                            if (e.Parameters.Length >= 3)
                            {
                                bool isUserMode = false;
                                foreach(char c in modeStr)
                                {
                                    if ("qaohv".Contains(c)) isUserMode = true;
                                }

                                if (isUserMode)
                                {
                                    var targetNick = e.Parameters[2];
                                    var user = channel.Users.FirstOrDefault(u => u.Nickname.Equals(targetNick, StringComparison.OrdinalIgnoreCase));
                                    if (user != null)
                                    {
                                        // Simple logic: Apply highest rank if multiple? 
                                        // Or just apply the specific change. 
                                        // Since we only store one prefix, we should try to determine the "best" one.
                                        // A robust client tracks all modes per user. Here we just map the latest change or best guess.
                                        
                                        if (modeStr.Contains("+q")) user.Prefix = "~";
                                        else if (modeStr.Contains("-q") && user.Prefix == "~") user.Prefix = "";
                                        
                                        else if (modeStr.Contains("+a")) user.Prefix = "&";
                                        else if (modeStr.Contains("-a") && user.Prefix == "&") user.Prefix = "";

                                        else if (modeStr.Contains("+o")) user.Prefix = "@";
                                        else if (modeStr.Contains("-o") && user.Prefix == "@") user.Prefix = "";
                                        
                                        else if (modeStr.Contains("+h")) user.Prefix = "%";
                                        else if (modeStr.Contains("-h") && user.Prefix == "%") user.Prefix = "";

                                        else if (modeStr.Contains("+v")) user.Prefix = "+";
                                        else if (modeStr.Contains("-v") && user.Prefix == "+") user.Prefix = "";
                                    }
                                    
                                    var setter = GetNickFromPrefix(e.Prefix);
                                    channel.AddMessage(new ChatMessage 
                                    { 
                                        Sender = "", 
                                        Content = $"* {setter} sets mode {modeStr} {targetNick}", 
                                        Timestamp = DateTime.Now,
                                        IsIncoming = true,
                                        IsSystem = true
                                    });
                                }
                                else
                                {
                                    // Channel mode
                                    channel.AddMessage(new ChatMessage 
                                    { 
                                        Sender = "", 
                                        Content = $"* {GetNickFromPrefix(e.Prefix)} sets mode {modeStr} {e.Parameters[2]}", 
                                        Timestamp = DateTime.Now,
                                        IsIncoming = true,
                                        IsSystem = true
                                    });
                                }
                            }
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
                            // Check for prefixes: ~, &, @, %, +
                            if (nick.StartsWith("~") || nick.StartsWith("&") || nick.StartsWith("@") || nick.StartsWith("%") || nick.StartsWith("+"))
                            {
                                prefix = nick.Substring(0, 1);
                                cleanNick = nick.Substring(1);
                            }
                            
                            lock (_channelListLock) // Re-use lock or ensure thread safety? ObservableCollection typically UI thread.
                            {
                                // Check if user exists to update prefix or add new
                                var existingUser = channel.Users.FirstOrDefault(u => u.Nickname == cleanNick);
                                if (existingUser != null)
                                {
                                    if(string.IsNullOrEmpty(existingUser.Prefix)) existingUser.Prefix = prefix;
                                }
                                else
                                {
                                     channel.AddUser(new IrcUser(cleanNick) { Prefix = prefix });
                                }
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
            else if (e.Command == "323") // RPL_LISTEND
            {
                // End of list - Sort by user count descending
                lock (_channelListLock)
                {
                    _allAvailableChannels.Sort((a, b) => b.UserCount.CompareTo(a.UserCount));
                }
                
                RefreshFilteredAvailableChannels();
                return; // Don't show in chat log
            }

            // Fallback: show unknown messages in the server tab
            Channels[0].AddMessage(new ChatMessage 
            { 
                Sender = "", 
                Content = e.RawMessage, 
                Timestamp = DateTime.Now,
                IsIncoming = true,
                IsSystem = true
            });
        }

        private void RefreshFilteredAvailableChannels()
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                IEnumerable<IrcChannelInfo> source;
                lock (_channelListLock)
                {
                    if (string.IsNullOrWhiteSpace(ChannelSearchText))
                    {
                        source = _allAvailableChannels.Take(500).ToList();
                    }
                    else
                    {
                        source = _allAvailableChannels
                            .Where(c => c.Name.Contains(ChannelSearchText, StringComparison.OrdinalIgnoreCase) || 
                                        c.Topic.Contains(ChannelSearchText, StringComparison.OrdinalIgnoreCase))
                            .Take(200)
                            .ToList();
                    }
                }
                
                FavoriteAvailableChannels.Clear();
                OtherAvailableChannels.Clear();

                foreach (var c in source)
                {
                    if (c.IsFavorite) FavoriteAvailableChannels.Add(c);
                    else OtherAvailableChannels.Add(c);
                }
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
                 _dispatcherQueue.TryEnqueue(() => 
                 {
                      Channels.Add(newChan);
                      if (InitialFavoriteChannels.Contains(name))
                      {
                          newChan.IsFavorite = true;
                          FavoriteChannels.Add(newChan);
                      }
                      else
                      {
                          OtherChannels.Add(newChan);
                      }
                  });
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
        
        private string _channelSearchText = "";
        
        private List<IrcChannelInfo> _allAvailableChannels = new List<IrcChannelInfo>();
        private object _channelListLock = new object();
        
        public ObservableCollection<IrcChannelInfo> FavoriteAvailableChannels { get; } = new ObservableCollection<IrcChannelInfo>();
        public ObservableCollection<IrcChannelInfo> OtherAvailableChannels { get; } = new ObservableCollection<IrcChannelInfo>();
        
        private async void ExecuteRefreshChannelList()
        {
            lock (_channelListLock)
            {
                _allAvailableChannels.Clear();
            }
            _dispatcherQueue.TryEnqueue(() => 
            {
                FavoriteAvailableChannels.Clear();
                OtherAvailableChannels.Clear();
            });
            await _ircService!.ListChannelsAsync();
        }

        private void ExecuteJoinChannel(string channelName)
        {
            if (!string.IsNullOrWhiteSpace(channelName))
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
        }

        private void ExecuteKickUser(IrcUser user)
        {
             if (user != null && SelectedChannel != null)
             {
                 _ = _ircService?.SendRawAsync($"KICK {SelectedChannel.Name} {user.Nickname} :Kicked by admin");
             }
        }

        private void ExecuteBanUser(IrcUser user)
        {
             if (user != null && SelectedChannel != null)
             {
                 _ = _ircService?.SendRawAsync($"MODE {SelectedChannel.Name} +b {user.Nickname}!*@*");
                 _ = _ircService?.SendRawAsync($"KICK {SelectedChannel.Name} {user.Nickname} :Banned by admin");
             }
        }

        private void ExecuteWhoisUser(IrcUser user)
        {
             if (user != null)
             {
                 _ = _ircService?.SendRawAsync($"WHOIS {user.Nickname}");
             }
        }

        public ServerConfig ToConfig()
        {
            return new ServerConfig
            {
                ServerName = ServerName,
                Address = _address,
                Port = _port,
                Nick = CurrentNick,
                Realname = _realname,
                Password = _password,
                AutoJoinChannel = _autoJoinChannel,
                FavoriteChannels = FavoriteChannels.Select(c => c.Name).ToList()
            };
        }
        private void ExecuteKillUser(IrcUser user)
        {
            if (user == null) return;
            RequestKillDialog?.Invoke(this, user);
        }

        private void ExecuteGlineUser(IrcUser user)
        {
            if (user == null) return;
            RequestGlineDialog?.Invoke(this, user);
        }

        public event EventHandler? RequestRemove;

        private void ExecuteConnect()
        {
            HandleConnection(_address, _port, CurrentNick, _realname, _password);
        }

        private void ExecuteDisconnect()
        {
            Disconnect("KonnectChat IRC Desktop Client");
        }

        private void ExecuteRemoveServer()
        {
            RequestRemove?.Invoke(this, EventArgs.Empty);
        }

        public void PerformKill(IrcUser user, string reason)
        {
            if (user == null) return;
            // KILL <nick> <reason>
            _ = _ircService?.SendRawAsync($"KILL {user.Nickname} :{reason}");
        }

        public void PerformGline(IrcUser user, string mask, long durationSeconds, string reason)
        {
            if (user == null) return;
            // GLINE <mask> <duration> <reason>
            // Note: Command syntax varies by IRCD. Standard often: GLINE <mask> <duration> :<reason>
            // Some use +duration. Assuming standard numeric duration.
            _ = _ircService?.SendRawAsync($"GLINE {mask} {durationSeconds} :{reason}");
        }
    }
}
