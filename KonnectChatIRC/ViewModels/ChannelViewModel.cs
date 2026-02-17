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
        private readonly DispatcherQueue _dispatcherQueue;

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

        public ObservableCollection<ChatMessage> Messages { get; } = new ObservableCollection<ChatMessage>();
        public ObservableCollection<IrcUser> Users { get; } = new ObservableCollection<IrcUser>();
        public ObservableCollection<UserGroup> GroupedUsers { get; } = new ObservableCollection<UserGroup>();

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
            _opsGroup = new UserGroup("Operators");
            _halfOpsGroup = new UserGroup("Half-Ops");
            _voiceGroup = new UserGroup("Voice");
            _usersGroup = new UserGroup("Users");

            GroupedUsers.Add(_opsGroup);
            GroupedUsers.Add(_halfOpsGroup);
            GroupedUsers.Add(_voiceGroup);
            GroupedUsers.Add(_usersGroup);
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
            switch(user.Rank)
            {
                case 0: _opsGroup?.Add(user); break;
                case 1: _halfOpsGroup?.Add(user); break;
                case 2: _voiceGroup?.Add(user); break;
                default: _usersGroup?.Add(user); break;
            }
        }

        private void RemoveFromGroups(IrcUser user)
        {
            _opsGroup?.Remove(user);
            _halfOpsGroup?.Remove(user);
            _voiceGroup?.Remove(user);
            _usersGroup?.Remove(user);
        }
    }
}
