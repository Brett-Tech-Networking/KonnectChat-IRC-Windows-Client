using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace KonnectChatIRC.Converters
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public bool IsInverted { get; set; }

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool b)
            {
                if (IsInverted) b = !b;
                return b ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
