using Microsoft.UI.Xaml.Data;
using Microsoft.UI;
using Windows.UI;
using System;

namespace KonnectChatIRC.Converters
{
    public class StatusToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool isOnline)
            {
                return isOnline ? Colors.LimeGreen : Colors.Crimson;
            }
            return Colors.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
