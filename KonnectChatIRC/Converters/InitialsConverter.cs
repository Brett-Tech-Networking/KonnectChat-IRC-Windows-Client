using Microsoft.UI.Xaml.Data;
using System;

namespace KonnectChatIRC.Converters
{
    public class InitialsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string s && !string.IsNullOrEmpty(s))
            {
                return s.Substring(0, 1).ToUpperInvariant();
            }
            return "#";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
