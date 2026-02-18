using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Windows.Storage;

namespace KonnectChatIRC.Services
{
    public class AppSettings : INotifyPropertyChanged
    {
        private static readonly Lazy<AppSettings> _instance = new(() => new AppSettings());
        public static AppSettings Instance => _instance.Value;

        private ApplicationDataContainer? _localSettings;

        private AppSettings()
        {
            try
            {
                _localSettings = ApplicationData.Current.LocalSettings;
                _timestampFormat = _localSettings.Values["TimestampFormat"] as string ?? "HH:mm:ss";
            }
            catch
            {
                // ApplicationData not ready yet — use defaults
                _timestampFormat = "HH:mm:ss";
            }
        }

        private ApplicationDataContainer? GetSettings()
        {
            if (_localSettings == null)
            {
                try { _localSettings = ApplicationData.Current.LocalSettings; } catch { }
            }
            return _localSettings;
        }

        private string _timestampFormat;

        /// <summary>
        /// Timestamp format: "HH:mm:ss" or "HH:mm"
        /// </summary>
        public string TimestampFormat
        {
            get => _timestampFormat;
            set
            {
                if (_timestampFormat != value)
                {
                    _timestampFormat = value;
                    try { var s = GetSettings(); if (s != null) s.Values["TimestampFormat"] = value; } catch { }
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Whether to show seconds in timestamp
        /// </summary>
        public bool ShowSeconds
        {
            get => TimestampFormat == "HH:mm:ss";
            set
            {
                TimestampFormat = value ? "HH:mm:ss" : "HH:mm";
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
