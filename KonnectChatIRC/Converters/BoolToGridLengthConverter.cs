using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace KonnectChatIRC.Converters
{
    public class BoolToGridLengthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool isCollapsed && isCollapsed)
            {
                return new GridLength(0);
            }
            return new GridLength(200); // Default width
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
