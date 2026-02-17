using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives; // Required for FlyoutBase
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Windowing; // Required for AppWindow
using System;
using KonnectChatIRC.ViewModels;
using KonnectChatIRC.Models; // Required for IrcUser
using WinRT.Interop; // Required for WindowNative

namespace KonnectChatIRC
{
    public sealed partial class MainWindow : Window
    {
        public MainViewModel Handle { get; }

        public MainWindow()
        {
            this.InitializeComponent();
            Handle = new MainViewModel();
            if (this.Content is FrameworkElement fe)
            {
                fe.DataContext = Handle;
            }

            // --- START: Set Window to Maximize (Fullscreen) ---
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            Microsoft.UI.WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

            if (appWindow != null)
            {
                var presenter = appWindow.Presenter as OverlappedPresenter;
                if (presenter != null)
                {
                    presenter.Maximize();
                }
            }
            // --- END: Set Window to Maximize ---
        }

        private void ChatInput_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                if (Handle.SelectedServer != null && Handle.SelectedServer.SendCommand.CanExecute(ChatInput.Text))
                {
                    Handle.SelectedServer.SendCommand.Execute(ChatInput.Text);
                    ChatInput.Text = "";
                }
            }
        }

        private void ChatList_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is ListView lv)
            {
                // Scroll to bottom initially
                if (lv.Items.Count > 0)
                {
                    lv.ScrollIntoView(lv.Items[lv.Items.Count - 1]);
                }

                // Subscribe to VectorChanged to scroll on new messages
                lv.Items.VectorChanged += (s, args) =>
                {
                    if (args.CollectionChange == Windows.Foundation.Collections.CollectionChange.ItemInserted)
                    {
                        // Use DispatcherQueue to ensure UI is updated before scrolling
                        lv.DispatcherQueue.TryEnqueue(() =>
                        {
                            if (lv.Items.Count > 0)
                            {
                                lv.ScrollIntoView(lv.Items[lv.Items.Count - 1]);
                            }
                        });
                    }
                };
            }
        }

        private void ChannelListDialog_RefreshClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
            if (Handle.SelectedServer != null)
            {
                Handle.SelectedServer.RefreshChannelListCommand.Execute(null);
                args.Cancel = true; // Stay open
            }
        }

        private async void DiscoverChannels_Click(object sender, RoutedEventArgs e)
        {
            ChannelListDialog.XamlRoot = this.Content.XamlRoot;
            await ChannelListDialog.ShowAsync();
        }

        private void ChannelSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Simple filtering logic could go here, or handled via VM
        }

        private void JoinChannelFromList_Click(object sender, RoutedEventArgs e)
        {
            ChannelListDialog.Hide();
        }

        private async void ShowAdvancedWhois_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is IrcUser user)
            {
                if (this.Content is FrameworkElement root && root.DataContext is MainViewModel mainVM)
                {
                    mainVM.SelectedWhoisUser = user;
                }
                
                WhoisDetailDialog.XamlRoot = this.Content.XamlRoot;
                await WhoisDetailDialog.ShowAsync();
            }
        }

        private void WhoisDetailChannel_Click(object sender, RoutedEventArgs e)
        {
            if (sender is HyperlinkButton hb && hb.Content is string channelName)
            {
                if (Handle.SelectedServer != null)
                {
                    Handle.SelectedServer.JoinChannelCommand.Execute(channelName);
                    WhoisDetailDialog.Hide();
                }
            }
        }

        private void UserItem_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if (sender is FrameworkElement fe)
            {
                // Auto-trigger WHOIS
                if (fe.DataContext is IrcUser user)
                {
                    // Find the MainViewModel via the root Grid
                    if (this.Content is FrameworkElement root && root.DataContext is MainViewModel mainVM)
                    {
                        if (mainVM.SelectedServer != null)
                        {
                            mainVM.SelectedServer.WhoisUserCommand.Execute(user);
                        }
                    }
                }
                
                FlyoutBase.ShowAttachedFlyout(fe);
            }
        }
    }

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