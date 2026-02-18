using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using System;
using System.Text.RegularExpressions;
using Windows.System;

namespace KonnectChatIRC.Helpers
{
    public static class Linkify
    {
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.RegisterAttached("Text", typeof(string), typeof(Linkify), new PropertyMetadata(null, OnTextChanged));

        public static string GetText(DependencyObject obj) => (string)obj.GetValue(TextProperty);
        public static void SetText(DependencyObject obj, string value) => obj.SetValue(TextProperty, value);

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is RichTextBlock richText)
            {
                richText.Blocks.Clear();
                var text = e.NewValue as string;
                if (string.IsNullOrEmpty(text)) return;

                var paragraph = new Paragraph();
                
                // Regex for URLs
                var urlRegex = new Regex(@"(https?://[^\s<>""'{}|\\^`[\]]+)", RegexOptions.IgnoreCase);
                var lastIndex = 0;

                foreach (Match match in urlRegex.Matches(text))
                {
                    // Add text before the link
                    if (match.Index > lastIndex)
                    {
                        paragraph.Inlines.Add(new Run { Text = text.Substring(lastIndex, match.Index - lastIndex) });
                    }

                    // Add the link
                    var link = new Hyperlink { NavigateUri = new Uri(match.Value) };
                    link.Inlines.Add(new Run { Text = match.Value });
                    // WinUI 3 Hyperlink handles Click/Navigate automatically if NavigateUri is set, 
                    // but we can also handle it manually for better control if needed.
                    paragraph.Inlines.Add(link);

                    lastIndex = match.Index + match.Length;
                }

                // Add remaining text
                if (lastIndex < text.Length)
                {
                    paragraph.Inlines.Add(new Run { Text = text.Substring(lastIndex) });
                }

                richText.Blocks.Add(paragraph);
            }
        }
    }
}
