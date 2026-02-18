namespace KonnectChatIRC.Services
{
    public static class AppSettings
    {
        // Default timestamp format, changed via Settings dialog
        public static string TimestampFormat { get; set; } = "HH:mm:ss";

        public static event System.Action? SettingsChanged;

        public static bool ShowSeconds
        {
            get => TimestampFormat == "HH:mm:ss";
            set
            {
                var newFmt = value ? "HH:mm:ss" : "HH:mm";
                if (TimestampFormat != newFmt)
                {
                    TimestampFormat = newFmt;
                    SettingsChanged?.Invoke();
                }
            }
        }

        public static void Load()
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                if (settings.Values.TryGetValue("TimestampFormat", out object? val) && val is string fmt)
                {
                    TimestampFormat = fmt;
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                settings.Values["TimestampFormat"] = TimestampFormat;
            }
            catch { }
        }
    }
}
