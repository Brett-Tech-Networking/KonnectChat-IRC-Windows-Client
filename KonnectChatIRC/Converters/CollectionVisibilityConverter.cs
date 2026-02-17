using System;
using System.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace KonnectChatIRC.Converters
{
    public class CollectionVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is ICollection collection)
            {
                return collection.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            if (value is int count)
            {
                return count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
