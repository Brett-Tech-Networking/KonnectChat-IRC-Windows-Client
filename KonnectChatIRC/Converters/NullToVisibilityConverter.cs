using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace KonnectChatIRC.Converters
{
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool invert = parameter != null && parameter.ToString() == "Invert";
            bool isNull = value == null;

            if (invert)
                return isNull ? Visibility.Visible : Visibility.Collapsed;

            return isNull ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
