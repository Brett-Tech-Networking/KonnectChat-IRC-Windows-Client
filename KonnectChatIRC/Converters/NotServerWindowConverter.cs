using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace KonnectChatIRC.Converters
{
    public class NotServerWindowConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string ps && ps == "Server")
                return Visibility.Collapsed;
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
