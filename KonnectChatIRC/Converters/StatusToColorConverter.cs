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
                return isOnline ? Colors.LimeGreen : Color.FromArgb(255, 255, 165, 0); // Online: Green, Away: Amber
            }

            if (value is string prefix)
            {
                if (prefix.Contains("~")) return Color.FromArgb(255, 255, 82, 82); // Red (Owner)
                if (prefix.Contains("&")) return Color.FromArgb(255, 255, 159, 28); // Orange (Admin)
                if (prefix.Contains("@")) return Color.FromArgb(255, 63, 169, 245); // Blue (Op)
                if (prefix.Contains("%")) return Color.FromArgb(255, 255, 165, 0); // Orange (HalfOp)
                if (prefix.Contains("+")) return Color.FromArgb(255, 50, 205, 50); // LimeGreen (Voice)
            }

            // Default text color (White/LightGray depending on theme, but here returning specific)
            return Color.FromArgb(255, 220, 220, 220); 
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
