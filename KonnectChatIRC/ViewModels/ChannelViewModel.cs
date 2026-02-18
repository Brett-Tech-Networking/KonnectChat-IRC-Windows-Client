using System;
using System.Collections.ObjectModel;
using System.Linq;
using KonnectChatIRC.Models;
using KonnectChatIRC.MVVM;
using Microsoft.UI.Dispatching;

namespace KonnectChatIRC.ViewModels
{
    public class ChannelViewModel : ViewModelBase
    {
        private string _name;
        private string _topic = "No Topic";
        private bool _isFavorite;
        private string _userSearchText = string.Empty;
        private readonly DispatcherQueue _dispatcherQueue;

        public bool IsFavorite
        {
            get => _isFavorite;
            set => SetProperty(ref _isFavorite, value);
        }

        public string UserSearchText
        {
            get => _userSearchText;
            set
            {
                if (SetProperty(ref _userSearchText, value))
                {
                    RefreshFilteredUsers();
                }
            }
        }

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string Topic
        {
            get => _topic;
            set => SetProperty(ref _topic, value);
        }

        private int _unreadCount;
        public int UnreadCount
        {
            get => _unreadCount;
            set => SetProperty(ref _unreadCount, value);
        }

        public ObservableCollection<ChatMessage> Messages { get; } = new ObservableCollection<ChatMessage>();
        public ObservableCollection<IrcUser> Users { get; } = new ObservableCollection<IrcUser>();
        public ObservableCollection<UserGroup> GroupedUsers { get; } = new ObservableCollection<UserGroup>();

        private UserGroup? _ownerGroup;
        private UserGroup? _adminGroup;
        private UserGroup? _opsGroup;
        private UserGroup? _halfOpsGroup;
        private UserGroup? _voiceGroup;
        private UserGroup? _usersGroup;

        public ChannelViewModel(string name)
        {
            _name = name;
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            InitializeGroups();
        }

        private void InitializeGroups()
        {
            _ownerGroup = new UserGroup("Owners");
            _adminGroup = new UserGroup("Admins");
            _opsGroup = new UserGroup("Operators");
            _halfOpsGroup = new UserGroup("Half-Ops");
            _voiceGroup = new UserGroup("Voice");
            _usersGroup = new UserGroup("Users");
        }

        private void RefreshFilteredUsers()
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                GroupedUsers.Clear();
                _ownerGroup?.Clear();
                _adminGroup?.Clear();
                _opsGroup?.Clear();
                _halfOpsGroup?.Clear();
                _voiceGroup?.Clear();
                _usersGroup?.Clear();

                foreach (var user in Users)
                {
                    AddToGroup(user);
                }
            });
        }
        
        private void EnsureGroupVisible(UserGroup? group)
        {
            if (group != null && !GroupedUsers.Contains(group))
            {
                // Find correct insertion index based on priority: Owner > Admin > Op > HalfOps > Voice > Users
                int targetIndex = 0;
                
                if (group == _ownerGroup) targetIndex = 0;
                else if (group == _adminGroup)
                {
                    if (GroupedUsers.Contains(_ownerGroup!)) targetIndex = 1;
                    else targetIndex = 0;
                }
                else if (group == _opsGroup)
                {
                    targetIndex = 0;
                    if (GroupedUsers.Contains(_ownerGroup!)) targetIndex++;
                    if (GroupedUsers.Contains(_adminGroup!)) targetIndex++;
                }
                else if (group == _halfOpsGroup)
                {
                    targetIndex = 0;
                    if (GroupedUsers.Contains(_ownerGroup!)) targetIndex++;
                    if (GroupedUsers.Contains(_adminGroup!)) targetIndex++;
                    if (GroupedUsers.Contains(_opsGroup!)) targetIndex++;
                }
                else if (group == _voiceGroup)
                {
                    targetIndex = 0;
                    if (GroupedUsers.Contains(_ownerGroup!)) targetIndex++;
                    if (GroupedUsers.Contains(_adminGroup!)) targetIndex++;
                    if (GroupedUsers.Contains(_opsGroup!)) targetIndex++;
                    if (GroupedUsers.Contains(_halfOpsGroup!)) targetIndex++;
                }
                else // Users
                {
                    targetIndex = GroupedUsers.Count;
                }
                
                GroupedUsers.Insert(System.Math.Min(targetIndex, GroupedUsers.Count), group);
            }
        }

        private void CheckGroupEmpty(UserGroup? group)
        {
            if (group != null && group.Count == 0 && GroupedUsers.Contains(group))
            {
                GroupedUsers.Remove(group);
            }
        }

        public void AddMessage(ChatMessage message)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                Messages.Add(message);
            });
        }

        public void AddUser(IrcUser user)
        {
            _dispatcherQueue.TryEnqueue(() => 
            {
                Users.Add(user);
                AddToGroup(user);
                user.PropertyChanged += User_PropertyChanged;
            });
        }

        public void RemoveUser(string nickname)
        {
            _dispatcherQueue.TryEnqueue(() => 
            {
                var user = Users.FirstOrDefault(u => u.Nickname == nickname);
                if(user != null) 
                {
                    Users.Remove(user);
                    RemoveFromGroups(user);
                    user.PropertyChanged -= User_PropertyChanged;
                }
            });
        }

        private void User_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IrcUser.Rank))
            {
                _dispatcherQueue.TryEnqueue(() => 
                {
                    if (sender is IrcUser user)
                    {
                        RemoveFromGroups(user);
                        AddToGroup(user);
                    }
                });
            }
        }

        private void AddToGroup(IrcUser user)
        {
            if (!string.IsNullOrWhiteSpace(UserSearchText) && 
                !user.Nickname.Contains(UserSearchText, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            UserGroup? target = user.Rank switch
            {
                0 => _ownerGroup,
                1 => _adminGroup,
                2 => _opsGroup,
                3 => _halfOpsGroup,
                4 => _voiceGroup,
                _ => _usersGroup
            };

            if (target != null)
            {
                if (!target.Contains(user)) target.Add(user);
                EnsureGroupVisible(target);
            }
        }

        private void RemoveFromGroups(IrcUser user)
        {
            _ownerGroup?.Remove(user);
            _adminGroup?.Remove(user);
            _opsGroup?.Remove(user);
            _halfOpsGroup?.Remove(user);
            _voiceGroup?.Remove(user);
            _usersGroup?.Remove(user);

            CheckGroupEmpty(_ownerGroup);
            CheckGroupEmpty(_adminGroup);
            CheckGroupEmpty(_opsGroup);
            CheckGroupEmpty(_halfOpsGroup);
            CheckGroupEmpty(_voiceGroup);
            CheckGroupEmpty(_usersGroup);
        }
    }
}
