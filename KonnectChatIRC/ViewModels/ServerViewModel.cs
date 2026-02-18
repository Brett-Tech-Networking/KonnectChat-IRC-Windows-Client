using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using System.Text.RegularExpressions;
using KonnectChatIRC.Models;
using KonnectChatIRC.Services;
using KonnectChatIRC.MVVM;
using Microsoft.UI.Dispatching;
using System.Net;

namespace KonnectChatIRC.ViewModels
{
    public class ServerViewModel : ViewModelBase
    {
        private IrcClientService? _ircService;
        private string _serverName;
        private ChannelViewModel? _selectedChannel;
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
        public ObservableCollection<ChannelViewModel> PrivateMessages { get; } = new ObservableCollection<ChannelViewModel>();
        public ObservableCollection<ChannelViewModel> OtherChannels { get; } = new ObservableCollection<ChannelViewModel>();

        public ChannelViewModel? SelectedChannel
        {
            get => _selectedChannel;
            set
            {
                if (SetProperty(ref _selectedChannel, value))
                {
                    if (_selectedChannel != null)
                    {
                        _selectedChannel.UnreadCount = 0;
                    }
                }
            }
        }

        private bool _isIrcOp;
        
        public bool IsIrcOp 
        { 
            get => _isIrcOp; 
            set => SetProperty(ref _isIrcOp, value); 
        }

        private bool _isPrivateMessagingDisabled;
        public bool IsPrivateMessagingDisabled
        {
            get => _isPrivateMessagingDisabled;
            set => SetProperty(ref _isPrivateMessagingDisabled, value);
        }

        private bool _isSelfAway;
        public bool IsSelfAway
        {
            get => _isSelfAway;
            set { if (SetProperty(ref _isSelfAway, value)) OnPropertyChanged(nameof(IsSelfOnline)); }
        }

        // Computed for StatusToColorConverter binding (true=green, false=amber)
        public bool IsSelfOnline => !IsSelfAway;

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
        public ICommand OpUserCommand { get; }
        public ICommand DeopUserCommand { get; }
        public ICommand ChangeNickCommand { get; }
        public ICommand PartChannelCommand { get; }
        public ICommand ChangeTopicCommand { get; }
        public ICommand ToggleChannelModeCommand { get; }
        public ICommand AddBanCommand { get; }
        public ICommand RemoveBanCommand { get; }
        public ICommand OpenModerationCommand { get; }
        public ICommand SlapCommand { get; }
        public ICommand ToggleFavoriteCommand { get; }
        public ICommand StartPrivateChatCommand { get; }
        public ICommand ClosePrivateChatCommand { get; }
        public ICommand ToggleAwayCommand { get; }

        public event EventHandler<ChannelViewModel>? RequestModerationDialog;
        public event EventHandler<IrcUser>? RequestKillDialog;
        public event EventHandler<IrcUser>? RequestGlineDialog;
        public event EventHandler? RequestIdent;
        public event EventHandler? RequestOper;

        private const string DefaultQuitMessage = "KonnectChat IRC Desktop Client https://www.bretttechcoding.com/Projects/windows-apps/konnectchatirc";

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
            JoinChannelCommand = new RelayCommand(param => { if(param is string s) ExecuteJoinChannel(s); });
            WhoisUserCommand = new RelayCommand(param => { if(param is IrcUser u) ExecuteWhoisUser(u); });
            KickUserCommand = new RelayCommand(param => { if(param is IrcUser u) ExecuteKickUser(u); });
            BanUserCommand = new RelayCommand(param => { if(param is IrcUser u) ExecuteBanUser(u); });
            KillUserCommand = new RelayCommand(param => { if(param is IrcUser u) ExecuteKillUser(u); });
            GlineUserCommand = new RelayCommand(param => { if(param is IrcUser u) ExecuteGlineUser(u); });
            OpUserCommand = new RelayCommand(param => { if(param is IrcUser u) ExecuteOpUser(u); });
            DeopUserCommand = new RelayCommand(param => { if(param is IrcUser u) ExecuteDeopUser(u); });
            IdentCommand = new RelayCommand(_ => RequestIdent?.Invoke(this, EventArgs.Empty));
            OperCommand = new RelayCommand(_ => RequestOper?.Invoke(this, EventArgs.Empty));
            
            ToggleChannelModeCommand = new RelayCommand(param => { if(param is Tuple<ChannelViewModel, string, bool> t) ExecuteToggleMode(t.Item1, t.Item2, t.Item3); });
            AddBanCommand = new RelayCommand(param => { if(param is Tuple<ChannelViewModel, string> t) ExecuteAddBan(t.Item1, t.Item2); });
            RemoveBanCommand = new RelayCommand(param => { if(param is Tuple<ChannelViewModel, string> t) ExecuteRemoveBan(t.Item1, t.Item2); });
            OpenModerationCommand = new RelayCommand(param => { if(param is ChannelViewModel c) ExecuteOpenModeration(c); });
            SlapCommand = new RelayCommand(_ => ExecuteSlap());
            ToggleAwayCommand = new RelayCommand(_ => ExecuteToggleAway());

            _ircService.MessageReceived += OnMessageReceived;
            
            _ircService.WelcomeReceived += (s, e) => 
            {
                // NickServ Identify if password is set
                if (!string.IsNullOrEmpty(_password))
                {
                    _ = _ircService.SendRawAsync($"PRIVMSG NickServ :ID {_password}");
                }

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
                    // Auto-away: idle > 5 minutes = away
                    bool idleAway = info.IdleSeconds > 300 || info.IsAway;

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
                            user.IsIrcOp = info.IsOperator;
                            user.IsNetworkAdmin = info.IsNetworkAdmin;
                            user.IsAway = idleAway;
                            user.AwayMessage = info.AwayMessage;
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
            PartChannelCommand = new RelayCommand(chan => _ircService?.SendRawAsync($"PART {chan} :{DefaultQuitMessage}"));
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
                        if (_channelLookup.TryGetValue(channelName, out var chanVm))
                        {
                            chanVm.IsFavorite = isSetToFavorite;
                            // Move it
                            if (isSetToFavorite)
                            {
                                if (OtherChannels.Contains(chanVm)) OtherChannels.Remove(chanVm);
                                if (!FavoriteChannels.Contains(chanVm)) FavoriteChannels.Add(chanVm);
                            }
                            else
                            {
                                if (FavoriteChannels.Contains(chanVm)) FavoriteChannels.Remove(chanVm);
                                if (!OtherChannels.Contains(chanVm)) OtherChannels.Add(chanVm);
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

            StartPrivateChatCommand = new RelayCommand(param =>
            {
                if (param is string nick && !string.IsNullOrWhiteSpace(nick))
                {
                    ExecuteStartPrivateChat(nick);
                }
                else if (param is IrcUser user)
                {
                    ExecuteStartPrivateChat(user.Nickname, user.Hostname);
                }
            });

            ClosePrivateChatCommand = new RelayCommand(param =>
            {
                if (param is ChannelViewModel pm)
                {
                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        if (PrivateMessages.Contains(pm))
                        {
                            PrivateMessages.Remove(pm);
                            _channelLookup.TryRemove(pm.Name, out _);
                            if (SelectedChannel == pm)
                            {
                                SelectedChannel = Channels.FirstOrDefault();
                            }
                        }
                    });
                }
            });

            // Start connection
            HandleConnection(address, port, nick, realname, password);
        }

        private async void HandleConnection(string address, int port, string nick, string realname, string? password)
        {
            if (_ircService != null)
            {
                // Pass null for server password, as requested user wants password for NickServ only
            // The password is stored in _password and used in WelcomeReceived
            await _ircService.ConnectAsync(_address, _port, _currentNick, _realname, null);
            }
        }

        private void ExecuteSlap()
        {
            if (SelectedChannel == null || SelectedChannel.Name == "Server") return;

            var slaps = new[]
            {
                "slaps {0} with a large trout",
                "slaps {0} with a waffle iron",
                "slaps {0} around a bit with a large trout",
                "slaps {0} with a wet noodle",
                "slaps {0} with a rubber chicken",
                "slaps {0} with a velvet glove",
                "slaps {0} with a heavy book",
                "slaps {0} with a slice of pizza",
                "slaps {0} with a giant marshmallow",
                "slaps {0} with a feather",
                "launches a grand piano at {0}",
                "thwacks {0} with a 1,000,000 volt taser",
                "slaps {0} into next Tuesday",
                "drops an anvil on {0}'s head",
                "blasts {0} with a high-pressure fire hose",
                "slaps {0} with a moldy piece of cheese",
                "summons a meteor to crash into {0}",
                "slaps {0} with a rusty bucket",
                "yeets {0} into the sun",
                "slaps {0} with a frozen mackerel",
                "whacks {0} with a customized ban hammer",
                "slaps {0} with a bag of wet socks",
                "pokes {0} with a sharp stick",
                "slaps {0} with a soggy sandwich",
                "unleashes a hoard of angry squirrels on {0}",
                "slaps {0} with a literal ton of bricks",
                "smacks {0} with a giant rotating fan",
                "slaps {0} with the force of a thousand suns",
                "delivers a spinning backfist to {0}",
                "slaps {0} with a 404 error"
            };

            var random = new Random();
            var slapTemplate = slaps[random.Next(slaps.Length)];
            
            // Try to find a user to slap. If one is selected in User List, use them.
            // Otherwise, if it's a PM, use the PM partner.
            // Otherwise, just use 'everyone' or a placeholder if we can't find anyone?
            // The prompt says "SelectedUser".
            
            string targetUser = "everyone";
            
            if (SelectedChannel.SelectedUser != null)
            {
                targetUser = SelectedChannel.SelectedUser.Nickname;
            }
            else if (SelectedChannel.IsPrivate)
            {
                targetUser = SelectedChannel.Name;
            }
            
            // TODO: If we want to slap a user from the user list, we need to know which one is selected.
            // For now, let's send it.
            
            string actionText = string.Format(slapTemplate, targetUser);
            _ircService?.SendRawAsync($"PRIVMSG {SelectedChannel.Name} :\x01ACTION {actionText}\x01");
            
            SelectedChannel.AddMessage(new ChatMessage
            {
                Sender = CurrentNick,
                Content = actionText,
                Timestamp = DateTime.Now,
                IsIncoming = false,
                IsAction = true
            });
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
                        else if (cmd == "/j" && parts.Length > 1)
                        {
                            // Quick: /j #channel → /join #channel
                            _ircService?.JoinChannelAsync(parts[1]);
                        }
                        else if (cmd == "/k" && parts.Length > 1)
                        {
                            // Quick: /k user [reason] → KICK #channel user :reason
                            if (SelectedChannel != null && SelectedChannel.Name.StartsWith("#"))
                            {
                                string kickNick = parts[1];
                                string reason = parts.Length > 2 ? string.Join(" ", parts.Skip(2)) : "Kicked";
                                _ircService?.SendRawAsync($"KICK {SelectedChannel.Name} {kickNick} :{reason}");
                            }
                        }
                        else if (cmd == "/b" && parts.Length > 1)
                        {
                            // Quick: /b user → MODE #channel +b user!*@*
                            if (SelectedChannel != null && SelectedChannel.Name.StartsWith("#"))
                            {
                                string banNick = parts[1];
                                _ircService?.SendRawAsync($"MODE {SelectedChannel.Name} +b {banNick}!*@*");
                            }
                        }
                        else if (cmd == "/p")
                        {
                            // Quick: /p → PART current channel and remove from list
                            if (SelectedChannel != null && SelectedChannel.Name.StartsWith("#"))
                            {
                                var chanToPart = SelectedChannel;
                                _ircService?.SendRawAsync($"PART {chanToPart.Name} :{DefaultQuitMessage}");
                                _dispatcherQueue.TryEnqueue(() =>
                                {
                                    Channels.Remove(chanToPart);
                                    OtherChannels.Remove(chanToPart);
                                    FavoriteChannels.Remove(chanToPart);
                                    _channelLookup.TryRemove(chanToPart.Name, out _);
                                    if (SelectedChannel == chanToPart)
                                    {
                                        SelectedChannel = Channels.FirstOrDefault();
                                    }
                                });
                            }
                        }
                        else if (cmd == "/quit")
                        {
                            string msg = parts.Length > 1 ? text.Substring(6) : DefaultQuitMessage;
                            Disconnect(msg);
                        }
                        else if (cmd == "/me" && parts.Length > 1)
                        {
                            string actionText = string.Join(" ", parts.Skip(1));
                            string target = SelectedChannel.Name;

                            if (target != "Server")
                            {
                                var user = SelectedChannel.Users.FirstOrDefault(u => u.Nickname.Equals(CurrentNick, StringComparison.OrdinalIgnoreCase));
                                var myPrefix = user?.Prefix ?? "";
                                var msg = new ChatMessage
                                {
                                    Sender = CurrentNick,
                                    SenderPrefix = myPrefix,
                                    Content = actionText,
                                    Timestamp = DateTime.Now,
                                    IsIncoming = false,
                                    IsAction = true
                                };
                                SetImageUrlIfFound(msg, actionText);
                                SelectedChannel.AddMessage(msg);
                            }
                        }
                        else if (cmd == "/msg" && parts.Length > 2)
                        {
                            string target = parts[1];
                            string msgText = string.Join(" ", parts.Skip(2));
                             
                            ExecuteStartPrivateChat(target);
                            _ircService?.SendRawAsync($"PRIVMSG {target} :{msgText}");
                             
                            // Echo to local PM window
                            if (_channelLookup.TryGetValue(target, out var pmChan))
                            {
                                var pmMsg = new ChatMessage
                                {
                                    Sender = CurrentNick,
                                    SenderPrefix = "", // PMs don't use prefixes
                                    Content = msgText,
                                    Timestamp = DateTime.Now,
                                    IsIncoming = false
                                };
                                SetImageUrlIfFound(pmMsg, msgText);
                                pmChan.AddMessage(pmMsg);
                            }
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
                            var user = SelectedChannel.Users.FirstOrDefault(u => u.Nickname.Equals(CurrentNick, StringComparison.OrdinalIgnoreCase));
                            var myPrefix = user?.Prefix ?? "";
                            var msg = new ChatMessage 
                            { 
                                Sender = CurrentNick, 
                                SenderPrefix = myPrefix,
                                Content = text, 
                                Timestamp = DateTime.Now, 
                                IsIncoming = false 
                            };

                            SetImageUrlIfFound(msg, text);

                            SelectedChannel.AddMessage(msg);
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
                var content = WebUtility.HtmlDecode(e.Parameters[1]);
                var senderNick = GetNickFromPrefix(e.Prefix);

                // If the target is our own nick, this is a private message to us.
                // The "channel" tab should be named after the sender.
                bool isPm = target.Equals(CurrentNick, StringComparison.OrdinalIgnoreCase);
                string channelName = isPm ? senderNick : target;

                if (isPm)
                {
                    if (IsPrivateMessagingDisabled)
                    {
                        // Auto-reply and drop
                        _ircService?.SendMessageAsync(senderNick, "This user has Private Messaging Disabled, You're message will NOT be seen. Please try again later.");
                        return;
                    }

                    var senderHost = GetHostFromPrefix(e.Prefix);
                    ExecuteStartPrivateChat(senderNick, senderHost, select: false); 
                    // ExecuteStartPrivateChat handles creating/adding to PrivateMessages collection
                }
                
                var channel = GetOrCreateChannel(channelName); // This will return the PM channel if it exists
                if (channel != null)
                {
                    var senderUser = channel.Users.FirstOrDefault(u => u.Nickname.Equals(senderNick, StringComparison.OrdinalIgnoreCase));
                    string prefix = senderUser?.Prefix ?? "";
                    
                    bool isAction = false;
                    if (content.StartsWith("\x01ACTION") && content.EndsWith("\x01"))
                    {
                        isAction = true;
                        // Strip "\x01ACTION " (8 chars) and "\x01" (1 char)
                        if (content.Length > 9)
                        {
                            content = content.Substring(8, content.Length - 9);
                        }
                        else
                        {
                            content = "";
                        }
                    }

                    var msg = new ChatMessage
                    {
                        Sender = senderNick,
                        SenderPrefix = prefix,
                        Content = content,
                        Timestamp = DateTime.Now,
                        IsIncoming = true,
                        IsAction = isAction
                    };

                    SetImageUrlIfFound(msg, content);

                    channel.AddMessage(msg);

                    if (SelectedChannel != channel)
                    {
                        channel.UnreadCount++;
                    }
                }
            }
            else if (e.Command == "TAGMSG")
            {
                // TAGMSG is similar to PRIVMSG but often carries metadata/typing info
                // Some bridges use it for media info. For now, we mainly want to ensure 
                // it's not strictly ignored if it contains content we can render.
                if (e.Parameters.Length >= 2)
                {
                    var target = e.Parameters[0];
                    var content = WebUtility.HtmlDecode(e.Parameters[1]);
                    var senderNick = GetNickFromPrefix(e.Prefix);

                    bool isPm = target.Equals(CurrentNick, StringComparison.OrdinalIgnoreCase);
                    string channelName = isPm ? senderNick : target;

                    var channel = GetOrCreateChannel(channelName);
                    if (channel != null)
                    {
                        var msg = new ChatMessage
                        {
                            Sender = senderNick,
                            Content = content,
                            Timestamp = DateTime.Now,
                            IsIncoming = true,
                            IsSystem = false
                        };
                        SetImageUrlIfFound(msg, content);
                        channel.AddMessage(msg);
                    }
                }
            }
            else if (e.Command == "NOTICE")
            {
                var target = e.Parameters[0];
                var content = WebUtility.HtmlDecode(e.Parameters[1]);
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
                    var msg = new ChatMessage 
                    { 
                        Sender = e.Prefix ?? "Notice", 
                        Content = content,
                        Timestamp = DateTime.Now,
                        IsIncoming = true,
                        IsSystem = true // Notices should usually be system-width
                    };
                    
                    SetImageUrlIfFound(msg, content);
                    channel.AddMessage(msg);
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
                            _dispatcherQueue.TryEnqueue(() =>
                            {
                                // Handle user mode changes (Owner/Admin/OP/HalfOp/Voice)
                                // Modes: q(Owner), a(Admin), o(Op), h(HalfOp), v(Voice)
                                // Standard implementation: iterate modes and consume args
                                int argIndex = 2;
                                bool adding = true;

                                foreach (char c in modeStr)
                                {
                                    if (c == '+')
                                    {
                                        adding = true; 
                                        continue;
                                    }
                                    if (c == '-')
                                    {
                                        adding = false;
                                        continue;
                                    }

                                    if ("qaohv".Contains(c))
                                    {
                                        if (argIndex < e.Parameters.Length)
                                        {
                                        var targetNick = e.Parameters[argIndex++];
                                        var user = channel.Users.FirstOrDefault(u => u.Nickname.Equals(targetNick, StringComparison.OrdinalIgnoreCase));
                                        
                                        if (user != null)
                                        {
                                            // Update prefix based on highest rank logic or simple replacement
                                            // Priority: ~ > & > @ > % > +
                                            // If adding, set prefix. If removing, only clear if it matches current.
                                            // NOTE: simplified logic for now. 
                                            // Ideally we track all modes, but here we just update the specific one.
                                            
                                            if (c == 'q')
                                            {
                                                if (adding) user.AddPrefix('~');
                                                else user.RemovePrefix('~');
                                            }
                                            else if (c == 'a')
                                            {
                                                if (adding) user.AddPrefix('&');
                                                else user.RemovePrefix('&');
                                            }
                                            else if (c == 'o')
                                            {
                                                if (adding) user.AddPrefix('@');
                                                else user.RemovePrefix('@');
                                            }
                                            else if (c == 'h')
                                            {
                                                if (adding) user.AddPrefix('%');
                                                else user.RemovePrefix('%');
                                            }
                                            else if (c == 'v')
                                            {
                                                if (adding) user.AddPrefix('+');
                                                else user.RemovePrefix('+');
                                            }
                                        }
                                        else
                                        {
                                            // Debug log removed
                                        }

                                        // Log the mode change
                                        var setter = GetNickFromPrefix(e.Prefix);
                                        string action = adding ? "gives" : "removes";
                                        string modeName = c switch { 'q' => "Owner", 'a' => "Admin", 'o' => "Operator", 'h' => "Half-Op", 'v' => "Voice", _ => c.ToString() };
                                        
                                        // Only log generic text update if complex mode, otherwise user list update handles visual
                                        // Actually, let's keep the standard IRC log format
                                    }
                                }
                                else if (c == 'b')
                                {
                                    if (argIndex < e.Parameters.Length)
                                    {
                                        var mask = e.Parameters[argIndex++];
                                        _dispatcherQueue.TryEnqueue(() => 
                                        {
                                            if (adding)
                                            {
                                                if (!channel.BanList.Contains(mask)) channel.BanList.Add(mask);
                                            }
                                            else
                                            {
                                                channel.BanList.Remove(mask);
                                            }
                                        });
                                    }
                                }
                                else if ("kl".Contains(c))
                                {
                                    // limit (l) always takes arg on set, sometimes on unset? k always takes arg?
                                    // standard: k always takes arg. l takes arg on set.
                                    if (c == 'k')
                                    {
                                        if (argIndex < e.Parameters.Length) argIndex++;
                                    }
                                    if (c == 'l' && adding)
                                    {
                                        if (argIndex < e.Parameters.Length) argIndex++;
                                    }
                                }
                                // Other modes might update channel modes
                            }
                            });

                            // Update the generic channel modes string (flawed but sufficient for simple display)
                            channel.UpdateModes(modeStr);

                            // Add a system message summary
                            channel.AddMessage(new ChatMessage 
                            { 
                                Sender = "", 
                                Content = $"* {GetNickFromPrefix(e.Prefix)} sets mode {modeStr} {string.Join(" ", e.Parameters.Skip(2))}", 
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
                            // Check for prefixes: ~, &, @, %, +
                            if (nick.StartsWith("~") || nick.StartsWith("&") || nick.StartsWith("@") || nick.StartsWith("%") || nick.StartsWith("+"))
                            {
                                prefix = nick.Substring(0, 1);
                                cleanNick = nick.Substring(1);
                            }
                            
                            lock (_channelListLock) // Re-use lock or ensure thread safety? ObservableCollection typically UI thread.
                            {
                                // Check if user exists to update prefix or add new
                                var existingUser = channel.Users.FirstOrDefault(u => u.Nickname.Equals(cleanNick, StringComparison.OrdinalIgnoreCase));
                                if (existingUser != null)
                                {
                                    existingUser.Prefix = prefix;
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
            else if (e.Command == "324") // RPL_CHANNELMODEIS
            {
                if (e.Parameters.Length >= 3)
                {
                    var channelName = e.Parameters[1];
                    var modeStr = e.Parameters[2];
                    var channel = GetOrCreateChannel(channelName);
                    if (channel != null)
                    {
                        _dispatcherQueue.TryEnqueue(() => 
                        {
                            channel.Modes = modeStr;
                            channel.NotifyModesChanged();
                        });
                    }
                }
            }
            else if (e.Command == "367") // RPL_BANLIST
            {
                if (e.Parameters.Length >= 3)
                {
                    var channelName = e.Parameters[1];
                    var mask = e.Parameters[2];
                    var channel = GetOrCreateChannel(channelName);
                    if (channel != null)
                    {
                        _dispatcherQueue.TryEnqueue(() => 
                        {
                            if (!channel.BanList.Contains(mask))
                                channel.BanList.Add(mask);
                        });
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
            // --- AWAY Status Handling ---
            else if (e.Command == "AWAY")
            {
                // :nick!user@host AWAY :away message  (user set away)
                // :nick!user@host AWAY                (user returned from away)
                var nick = GetNickFromPrefix(e.Prefix);
                if (!string.IsNullOrEmpty(nick))
                {
                    bool isAway = e.Parameters.Length > 0 && !string.IsNullOrEmpty(e.Parameters[0]);
                    string awayMsg = isAway ? e.Parameters[0] : "";
                    SetUserAwayStatus(nick, isAway, awayMsg);
                }
                return;
            }
            else if (e.Command == "301") // RPL_AWAY - user is away
            {
                // :server 301 mynick targetNick :away message
                if (e.Parameters.Length >= 3)
                {
                    var targetNick = e.Parameters[1];
                    var awayMsg = e.Parameters[2];
                    SetUserAwayStatus(targetNick, true, awayMsg);
                }
                return;
            }
            else if (e.Command == "305") // RPL_UNAWAY - we are no longer away
            {
                IsSelfAway = false;
                SetUserAwayStatus(CurrentNick, false, "");
                return;
            }
            else if (e.Command == "306") // RPL_NOWAWAY - we are now away
            {
                string awayMsg = e.Parameters.Length > 1 ? e.Parameters[1] : "Away";
                IsSelfAway = true;
                SetUserAwayStatus(CurrentNick, true, awayMsg);
                return;
            }

            // Log unhandled commands to Server tab for debugging
            if (!string.IsNullOrEmpty(e.Command) && !int.TryParse(e.Command, out _))
            {
                AddSystemMessage($"[Debug] Unhandled Command: {e.Command} {string.Join(" ", e.Parameters)}");
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

        private void SetImageUrlIfFound(ChatMessage msg, string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return;

            // 1. Direct Image Link Regex (Handles common extensions including .webp, .svg)
            var directMatch = Regex.Match(content, @"(https?://[^\s<>""'{}|\\^`[\]]+?\.(?:jpg|jpeg|png|gif|webp|svg)(?:\?[^\s<>""'{}|\\^`[\]]*?)?)", RegexOptions.IgnoreCase);
            if (directMatch.Success)
            {
                msg.ImageUrl = directMatch.Value;
                return;
            }

            // 2. Imgur Regex (Handles i.imgur.com/..., imgur.com/a/..., imgur.com/gallery/...)
            var imgurMatch = Regex.Match(content, @"(https?://(?:i\.)?imgur\.com/(?:a/|gallery/)?[a-z0-9]+)", RegexOptions.IgnoreCase);
            if (imgurMatch.Success)
            {
                var url = imgurMatch.Value;
                // If it's a direct imgur link without extension, we can often just append .png to it
                // but let's be safe and just use it as is for now as WinUI Image might handle it if it's i.imgur.com
                if (!url.Contains(".") || url.EndsWith(".com")) return; // Skip base domain
                
                if (url.Contains("i.imgur.com"))
                {
                     msg.ImageUrl = url;
                }
                else if (url.Contains("/a/") || url.Contains("/gallery/"))
                {
                    // For albums/galleries, we can't easily get the direct image without an API call
                    // but we can at least ensure the link is clickable (handled by Linkify).
                }
                else
                {
                    // Direct link to page, can often append .png to get preview
                    msg.ImageUrl = url + ".png";
                }
            }
        }

        private string GetNickFromPrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return "";
            var idx = prefix.IndexOf('!');
            return idx != -1 ? prefix.Substring(0, idx) : prefix;
        }

        private string? GetHostFromPrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix)) return null;
            var idx = prefix.IndexOf('@');
            return idx != -1 ? prefix.Substring(idx + 1) : null;
        }

        private void SetUserAwayStatus(string nick, bool isAway, string awayMessage)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                // Update user across all channels they appear in
                foreach (var channel in Channels)
                {
                    var user = channel.Users.FirstOrDefault(u => u.Nickname.Equals(nick, StringComparison.OrdinalIgnoreCase));
                    if (user != null)
                    {
                        user.IsAway = isAway;
                        user.AwayMessage = awayMessage;
                    }
                }
            });
        }

        private void ExecuteToggleAway()
        {
            if (IsSelfAway)
            {
                // Clear away - send AWAY with no message
                _ircService?.SendRawAsync("AWAY");
            }
            else
            {
                // Set away
                _ircService?.SendRawAsync("AWAY :Away");
            }
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

        private void ExecuteOpUser(IrcUser user)
        {
            if (user != null && SelectedChannel != null)
            {
                _ = _ircService?.SendRawAsync($"MODE {SelectedChannel.Name} +o {user.Nickname}");
            }
        }

        private void ExecuteDeopUser(IrcUser user)
        {
            if (user != null && SelectedChannel != null)
            {
                _ = _ircService?.SendRawAsync($"MODE {SelectedChannel.Name} -o {user.Nickname}");
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
            Disconnect(DefaultQuitMessage);
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

        public ICommand IdentCommand { get; }
        public ICommand OperCommand { get; }

        public void PerformIdent(string password)
        {
            if (!string.IsNullOrWhiteSpace(password))
            {
                // /ns id password -> PRIVMSG NickServ :ID password
                _ = _ircService?.SendRawAsync($"PRIVMSG NickServ :ID {password}");
            }
        }

        public void PerformOper(string nick, string password)
        {
            if (!string.IsNullOrWhiteSpace(nick) && !string.IsNullOrWhiteSpace(password))
            {
                // /oper nick password
                _ = _ircService?.SendRawAsync($"OPER {nick} {password}");
            }
        }

        private void ExecuteStartPrivateChat(string targetNick, string? targetHostname = null, bool select = true)
        {
            if (string.Equals(targetNick, CurrentNick, StringComparison.OrdinalIgnoreCase)) return;

            var pmKey = targetNick;
            
            // Create the PM channel and add to _channelLookup SYNCHRONOUSLY 
            // so that GetOrCreateChannel can find it immediately on the IRC thread.
            // _channelLookup is a ConcurrentDictionary, so this is thread-safe.
            if (!_channelLookup.ContainsKey(pmKey))
            {
                var pmChannel = new ChannelViewModel(targetNick)
                {
                    IsPrivate = true,
                    Topic = !string.IsNullOrEmpty(targetHostname) ? targetHostname : targetNick
                };

                if (_channelLookup.TryAdd(pmKey, pmChannel))
                {
                    // Dispatch UI collection updates to the UI thread
                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        // Add default users
                        pmChannel.AddUser(new IrcUser(targetNick) { Hostname = targetHostname ?? "" });
                        pmChannel.AddUser(new IrcUser(CurrentNick));
                        PrivateMessages.Add(pmChannel);

                        if (select)
                        {
                            SelectedChannel = pmChannel;
                        }
                    });
                }
            }
            else
            {
                // Update topic/hostname if we now have better data
                var existing = _channelLookup[pmKey];
                if (!string.IsNullOrEmpty(targetHostname))
                {
                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        if (existing.Topic == targetNick || existing.Topic == "No Topic" || string.IsNullOrEmpty(existing.Topic))
                        {
                            existing.Topic = targetHostname;
                        }
                        var user = existing.Users.FirstOrDefault(u => u.Nickname.Equals(targetNick, StringComparison.OrdinalIgnoreCase));
                        if (user != null) user.Hostname = targetHostname;
                    });
                }

                if (select)
                {
                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        SelectedChannel = _channelLookup[pmKey];
                    });
                }
            }
        }

        private void ExecuteOpenModeration(ChannelViewModel channel)
        {
            _dispatcherQueue.TryEnqueue(() => 
            {
                channel.BanList.Clear();
                RequestModerationDialog?.Invoke(this, channel);
            });

            // Fetch modes and ban list
            _ircService?.SendRawAsync($"MODE {channel.Name}");    // Channel modes
            _ircService?.SendRawAsync($"MODE {channel.Name} +b"); // Ban list
        }

        private void ExecuteToggleMode(ChannelViewModel channel, string mode, bool enable)
        {
            string op = enable ? "+" : "-";
            _ircService?.SendRawAsync($"MODE {channel.Name} {op}{mode}");
        }

        private void ExecuteAddBan(ChannelViewModel channel, string mask)
        {
            if (string.IsNullOrWhiteSpace(mask)) return;
            _ircService?.SendRawAsync($"MODE {channel.Name} +b {mask}");
        }

        private void ExecuteRemoveBan(ChannelViewModel channel, string mask)
        {
            if (string.IsNullOrWhiteSpace(mask)) return;
            _ircService?.SendRawAsync($"MODE {channel.Name} -b {mask}");
        }

        public void RefreshTimestamps()
        {
            foreach (var channel in Channels) channel.RefreshTimestamps();
            foreach (var channel in OtherChannels) channel.RefreshTimestamps();
            foreach (var channel in PrivateMessages) channel.RefreshTimestamps();
        }
    }
}
