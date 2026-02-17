using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage;
using KonnectChatIRC.Models;

namespace KonnectChatIRC.Services
{
    public class SettingsService
    {
        private const string SettingsFile = "servers.json";

        public static async Task SaveServersAsync(List<ServerConfig> servers)
        {
            try
            {
                var json = JsonSerializer.Serialize(servers);
                var folder = ApplicationData.Current.LocalFolder;
                var file = await folder.CreateFileAsync(SettingsFile, CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(file, json);
            }
            catch (Exception)
            {
                // Log error or ignore
            }
        }

        public static async Task<List<ServerConfig>> LoadServersAsync()
        {
            try
            {
                var folder = ApplicationData.Current.LocalFolder;
                var file = await folder.GetFileAsync(SettingsFile);
                var json = await FileIO.ReadTextAsync(file);
                return JsonSerializer.Deserialize<List<ServerConfig>>(json) ?? new List<ServerConfig>();
            }
            catch (FileNotFoundException)
            {
                return new List<ServerConfig>();
            }
            catch (Exception)
            {
                return new List<ServerConfig>();
            }
        }
    }
}
