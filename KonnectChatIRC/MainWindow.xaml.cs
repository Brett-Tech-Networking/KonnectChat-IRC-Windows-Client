using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives; // Required for FlyoutBase
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Windowing; // Required for AppWindow
using Microsoft.UI.Dispatching;
using System;
using KonnectChatIRC.ViewModels;
using KonnectChatIRC.Models; // Required for IrcUser
using System.Linq; // Required for FirstOrDefault
using WinRT.Interop; // Required for WindowNative

namespace KonnectChatIRC
{
    public sealed partial class MainWindow : Window
    {
        public MainViewModel Handle { get; }

        public MainWindow()
        {
            this.InitializeComponent();
            Services.AppSettings.Load();
            Handle = new MainViewModel();
            if (this.Content is FrameworkElement fe)
            {
                fe.DataContext = Handle;
            }

            Handle.PropertyChanged += MainViewModel_PropertyChanged;
            Handle.RequestSettingsDialog += Handle_RequestSettingsDialog;

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
                    // Remove default title bar but keep border for resizing
                    presenter.SetBorderAndTitleBar(true, false);
                }
            }
            // --- END: Set Window to Maximize ---

            // Enable dragging on the custom title bar
            AppTitleBar.PointerPressed += AppTitleBar_PointerPressed;
        }



        private async void Handle_RequestSettingsDialog(object? sender, EventArgs e)
        {
            if (SettingsDialog != null)
            {
                // Initialize timestamp toggle to current setting
                TimestampSecondsToggle.IsOn = Services.AppSettings.ShowSeconds;
                SettingsDialog.XamlRoot = this.Content.XamlRoot;
                await SettingsDialog.ShowAsync();
            }
        }

        private void TimestampSecondsToggle_Toggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            if (sender is Microsoft.UI.Xaml.Controls.ToggleSwitch toggle)
            {
                Services.AppSettings.ShowSeconds = toggle.IsOn;
                Services.AppSettings.Save();
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;

        private void AppTitleBar_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (e.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
            {
                ReleaseCapture();
                IntPtr hWnd = WindowNative.GetWindowHandle(this);
                SendMessage(hWnd, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            Microsoft.UI.WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Minimize();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private ServerViewModel? _currentServer;

        private void MainViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedServer))
            {
                if (_currentServer != null)
                {
                    _currentServer.RequestKillDialog -= Server_RequestKillDialog;
                    _currentServer.RequestGlineDialog -= Server_RequestGlineDialog;
                    _currentServer.RequestIdent -= Server_RequestIdent;
                    _currentServer.RequestOper -= Server_RequestOper;
                    _currentServer.RequestModerationDialog -= Server_RequestModerationDialog;
                }

                _currentServer = Handle.SelectedServer;

                if (_currentServer != null)
                {
                    _currentServer.RequestKillDialog += Server_RequestKillDialog;
                    _currentServer.RequestGlineDialog += Server_RequestGlineDialog;
                    _currentServer.RequestIdent += Server_RequestIdent;
                    _currentServer.RequestOper += Server_RequestOper;
                    _currentServer.RequestModerationDialog += Server_RequestModerationDialog;
                }
            }
        }

        private async void Server_RequestKillDialog(object? sender, IrcUser user)
        {
            var dialog = new ContentDialog
            {
                Title = $"Kill {user.Nickname}",
                PrimaryButtonText = "Kill",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.Content.XamlRoot
            };

            var stack = new StackPanel { Spacing = 10 };
            var reasonBox = new TextBox { Header = "Reason", PlaceholderText = "Spamming/Abuse" };
            stack.Children.Add(reasonBox);
            dialog.Content = stack;

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                var reason = reasonBox.Text;
                if (string.IsNullOrWhiteSpace(reason)) reason = "No reason given";
                _currentServer?.PerformKill(user, reason);
            }
        }

        private async void Server_RequestGlineDialog(object? sender, IrcUser user)
        {
            var dialog = new ContentDialog
            {
                Title = $"Gline {user.Nickname}",
                PrimaryButtonText = "Gline",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.Content.XamlRoot
            };

            var stack = new StackPanel { Spacing = 10 };
            
            // Mask
            var maskBox = new TextBox { Header = "Mask", Text = $"*@{(string.IsNullOrEmpty(user.Hostname) ? "*" : user.Hostname)}" };
            stack.Children.Add(maskBox);

            // Duration
            var durationCombo = new ComboBox { Header = "Duration", HorizontalAlignment = HorizontalAlignment.Stretch };
            durationCombo.Items.Add(new ComboBoxItem { Content = "5 Minutes", Tag = 300 });
            durationCombo.Items.Add(new ComboBoxItem { Content = "30 Minutes", Tag = 1800 });
            durationCombo.Items.Add(new ComboBoxItem { Content = "1 Hour", Tag = 3600 });
            durationCombo.Items.Add(new ComboBoxItem { Content = "1 Day", Tag = 86400 });
            durationCombo.Items.Add(new ComboBoxItem { Content = "7 Days", Tag = 604800 });
            durationCombo.SelectedIndex = 2; // Default 1 hour
            stack.Children.Add(durationCombo);

            // Reason
            var reasonBox = new TextBox { Header = "Reason", PlaceholderText = "Spamming/Abuse" };
            stack.Children.Add(reasonBox);

            dialog.Content = stack;

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                var mask = maskBox.Text;
                if (string.IsNullOrWhiteSpace(mask)) mask = $"*@{(string.IsNullOrEmpty(user.Hostname) ? "*" : user.Hostname)}";

                int duration = 3600;
                if (durationCombo.SelectedItem is ComboBoxItem item && item.Tag is int tagVal)
                {
                    duration = tagVal;
                }

                var reason = reasonBox.Text;
                if (string.IsNullOrWhiteSpace(reason)) reason = "No reason given";

                _currentServer?.PerformGline(user, mask, duration, reason);
            }
        }

        private async void Server_RequestIdent(object? sender, EventArgs e)
        {
            if (IdentDialog == null) return;
            
            IdentDialog.XamlRoot = this.Content.XamlRoot;
            IdentPasswordBox.Password = "";
            
            var result = await IdentDialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                _currentServer?.PerformIdent(IdentPasswordBox.Password);
            }
        }

        private async void Server_RequestOper(object? sender, EventArgs e)
        {
            if (OperDialog == null) return;
            
            OperDialog.XamlRoot = this.Content.XamlRoot;
            OperNickBox.Text = _currentServer?.CurrentNick ?? "";
            OperPasswordBox.Password = "";
            
            var result = await OperDialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                _currentServer?.PerformOper(OperNickBox.Text, OperPasswordBox.Password);
            }
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

        private async void UploadImage_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail;
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".gif");

            // For WinUI 3 on Desktop
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                try
                {
                    UploadButton.IsEnabled = false;
                    UploadInfoBar.IsOpen = true;
                    UploadInfoBar.Severity = InfoBarSeverity.Informational;
                    UploadInfoBar.Title = "Uploading...";
                    UploadInfoBar.Message = $"Uploading {file.Name} to catbox.moe";

                    // Upload the file
                    var url = await Services.ImageUploadService.UploadImageAsync(file);
                    
                    if (url != null)
                    {
                        UploadInfoBar.Severity = InfoBarSeverity.Success;
                        UploadInfoBar.Title = "Upload Successful";
                        UploadInfoBar.Message = "Image link attached to text box.";

                        // Copy URL to input
                        if (string.IsNullOrWhiteSpace(ChatInput.Text))
                        {
                            ChatInput.Text = url;
                        }
                        else
                        {
                            ChatInput.Text += " " + url;
                        }

                        // Focus input so user can type more text
                        ChatInput.Focus(FocusState.Programmatic);
                        ChatInput.SelectionStart = ChatInput.Text.Length;
                    }
                    else
                    {
                        UploadInfoBar.Severity = InfoBarSeverity.Error;
                        UploadInfoBar.Title = "Upload Failed";
                        UploadInfoBar.Message = "Could not upload image to catbox.moe. Please try again.";
                    }
                }
                catch (Exception ex)
                {
                    UploadInfoBar.Severity = InfoBarSeverity.Error;
                    UploadInfoBar.Title = "Upload Error";
                    UploadInfoBar.Message = ex.Message;
                }
                finally
                {
                    UploadButton.IsEnabled = true;
                }
            }
        }

        private void ChatList_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is ListView lv)
            {
                // Scroll to bottom helper
                Action scrollToBottom = () =>
                {
                    lv.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
                    {
                        if (lv.Items.Count > 0)
                        {
                            lv.ScrollIntoView(lv.Items[lv.Items.Count - 1]);
                        }
                    });
                };

                // ItemsUpdatingScrollMode="KeepLastItemInView" in XAML handles new items.
                // We just need to handle the initial load and channel switches.

                // Monitor channel/server changes to scroll to bottom once
                Handle.PropertyChanged += (s, args) =>
                {
                    if (args.PropertyName == nameof(MainViewModel.SelectedServer))
                    {
                        if (Handle.SelectedServer != null)
                        {
                            // We use a weak-like pattern or just ensure we don't double-subscribe 
                            // though for simplicity we just scroll when the top level selection changes.
                            scrollToBottom();
                        }
                    }
                };

                // The sub-property change (SelectedChannel) is trickier to handle without leaks 
                // but we can monitor it on the MainViewModel if we expose it or just handle it here.
                // For now, let's just make sure we scroll when the ItemsSource changes.
                lv.RegisterPropertyChangedCallback(ItemsControl.ItemsSourceProperty, (s, dp) =>
                {
                    scrollToBottom();
                });

                scrollToBottom();
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
                            
                            if (mainVM.SelectedServer.SelectedChannel != null)
                            {
                                mainVM.SelectedServer.SelectedChannel.SelectedUser = user;
                            }
                        }
                    }
                }
                
                FlyoutBase.ShowAttachedFlyout(fe);
            }
        }

        private async void Server_RequestModerationDialog(object? sender, ChannelViewModel channel)
        {
            if (ModerationDialog == null) return;
            
            ModerationDialog.XamlRoot = this.Content.XamlRoot;
            ModerationDialog.Title = $"Moderation: {channel.Name}";
            NewBanTextBox.Text = "";
            ChannelKeyBox.Text = "";
            ChannelLimitBox.Value = double.NaN;

            // Initialize topic editing
            ModerationTopicTextBox.Text = channel.Topic ?? "";
            var currentNick = Handle.SelectedServer.CurrentNick;
            var currentUser = channel.Users.FirstOrDefault(u => u.Nickname.Equals(currentNick, StringComparison.OrdinalIgnoreCase));
            bool isOp = currentUser != null && currentUser.IsOp;
            ModerationTopicEditStack.Visibility = isOp ? Visibility.Visible : Visibility.Collapsed;
            ModerationTopicStaticText.Visibility = isOp ? Visibility.Collapsed : Visibility.Visible;
            
            await ModerationDialog.ShowAsync();
        }

        private void Mode_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.Tag is string modeChar)
            {
                if (Handle.SelectedServer?.SelectedChannel != null)
                {
                    var channel = Handle.SelectedServer.SelectedChannel;
                    bool enable = cb.IsChecked ?? false;
                    Handle.SelectedServer.ToggleChannelModeCommand.Execute(new Tuple<ChannelViewModel, string, bool>(channel, modeChar, enable));
                }
            }
        }

        private void AddBan_Click(object sender, RoutedEventArgs e)
        {
            ExecuteAddBan();
        }

        private void NewBanTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                ExecuteAddBan();
            }
        }

        private void ExecuteAddBan()
        {
            string mask = NewBanTextBox.Text.Trim();
            if (string.IsNullOrEmpty(mask)) return;

            if (Handle.SelectedServer?.SelectedChannel != null)
            {
                Handle.SelectedServer.AddBanCommand.Execute(new Tuple<ChannelViewModel, string>(Handle.SelectedServer.SelectedChannel, mask));
                NewBanTextBox.Text = "";
            }
        }

        private void RemoveBan_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is string mask)
            {
                if (Handle.SelectedServer?.SelectedChannel != null)
                {
                    Handle.SelectedServer.RemoveBanCommand.Execute(new Tuple<ChannelViewModel, string>(Handle.SelectedServer.SelectedChannel, mask));
                }
            }
        }

        private void ChannelKeyBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                if (Handle.SelectedServer?.SelectedChannel != null)
                {
                    var channel = Handle.SelectedServer.SelectedChannel;
                    var key = ChannelKeyBox.Text.Trim();
                    if (string.IsNullOrEmpty(key))
                    {
                        Handle.SelectedServer?.ToggleChannelModeCommand.Execute(new Tuple<ChannelViewModel, string, bool>(channel, "k", false));
                    }
                    else
                    {
                        Handle.SelectedServer?.SendCommand.Execute($"/mode {channel.Name} +k {key}");
                    }
                }
            }
        }

        private void ChannelLimitBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (Handle.SelectedServer?.SelectedChannel != null)
            {
                var channel = Handle.SelectedServer.SelectedChannel;
                if (double.IsNaN(args.NewValue))
                {
                    Handle.SelectedServer?.ToggleChannelModeCommand.Execute(new Tuple<ChannelViewModel, string, bool>(channel, "l", false));
                }
                else
                {
                    Handle.SelectedServer?.SendCommand.Execute($"/mode {channel.Name} +l {(int)args.NewValue}");
                }
            }
        }

        private async void Topic_Click(object sender, RoutedEventArgs e)
        {
            if (Handle.SelectedServer?.SelectedChannel == null) return;

            var channel = Handle.SelectedServer.SelectedChannel;
            TopicTextBox.Text = channel.Topic ?? "";
            
            // Check if current user is OP in this channel
            var currentNick = Handle.SelectedServer.CurrentNick;
            var currentUser = channel.Users.FirstOrDefault(u => u.Nickname.Equals(currentNick, StringComparison.OrdinalIgnoreCase));
            
            TopicEditStack.Visibility = (currentUser != null && currentUser.IsOp) ? Visibility.Visible : Visibility.Collapsed;
            
            TopicEditDialog.XamlRoot = this.Content.XamlRoot;
            var result = await TopicEditDialog.ShowAsync();

            if (result == ContentDialogResult.Primary && TopicEditStack.Visibility == Visibility.Visible)
            {
                var newTopic = TopicTextBox.Text.Trim();
                Handle.SelectedServer.ChangeTopicCommand.Execute(newTopic);
            }
        }



        private void UpdateModerationTopic_Click(object sender, RoutedEventArgs e)
        {
            if (Handle.SelectedServer?.SelectedChannel != null)
            {
                var newTopic = ModerationTopicTextBox.Text.Trim();
                Handle.SelectedServer.ChangeTopicCommand.Execute(newTopic);
            }
        }

        private async void Nickname_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (Handle.SelectedServer == null) return;

            NewNickTextBox.Text = Handle.SelectedServer.CurrentNick;
            NickChangeDialog.XamlRoot = this.Content.XamlRoot;
            
            var result = await NickChangeDialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                var newNick = NewNickTextBox.Text.Trim();
                if (!string.IsNullOrEmpty(newNick) && newNick != Handle.SelectedServer.CurrentNick)
                {
                    Handle.SelectedServer.ChangeNickCommand.Execute(newNick);
                }
            }
        }

        private void FavoriteChannelsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FavoriteChannelsList.SelectedItem is ChannelViewModel selected)
            {
                if (Handle.SelectedServer != null)
                {
                    Handle.SelectedServer.SelectedChannel = selected;
                }
                OtherChannelsList.SelectedItem = null;
                PrivateMessagesList.SelectedItem = null;
            }
        }

        private void OtherChannelsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (OtherChannelsList.SelectedItem is ChannelViewModel selected)
            {
                if (Handle.SelectedServer != null)
                {
                    Handle.SelectedServer.SelectedChannel = selected;
                }
                FavoriteChannelsList.SelectedItem = null;
                PrivateMessagesList.SelectedItem = null;
            }
        }

        private void PrivateMessagesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PrivateMessagesList.SelectedItem is ChannelViewModel selected)
            {
                if (Handle.SelectedServer != null)
                {
                    Handle.SelectedServer.SelectedChannel = selected;
                }
                FavoriteChannelsList.SelectedItem = null;
                OtherChannelsList.SelectedItem = null;
            }
        }
    }
}