using System.Collections.ObjectModel;
using KonnectChatIRC.Models;

namespace KonnectChatIRC.ViewModels
{
    public class UserGroup : ObservableCollection<IrcUser>
    {
        public string Key { get; }

        public UserGroup(string key)
        {
            Key = key;
        }
    }
}
