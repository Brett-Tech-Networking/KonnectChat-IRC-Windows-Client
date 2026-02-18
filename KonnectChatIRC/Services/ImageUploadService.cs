using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Windows.Storage;

namespace KonnectChatIRC.Services
{
    public class ImageUploadService
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private const string CatboxUrl = "https://catbox.moe/user/api.php";

        static ImageUploadService()
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "KonnectChat-IRC-Client/1.0");
        }

        public static async Task<string?> UploadImageAsync(StorageFile file)
        {
            try
            {
                using var stream = await file.OpenStreamForReadAsync();
                using var content = new MultipartFormDataContent();
                
                content.Add(new StringContent("fileupload"), "reqtype");
                
                var fileContent = new StreamContent(stream);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
                content.Add(fileContent, "fileToUpload", file.Name);

                var response = await _httpClient.PostAsync(CatboxUrl, content);
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadAsStringAsync();
                    string url = result.Trim();
                    if (Uri.TryCreate(url, UriKind.Absolute, out _))
                    {
                        return url;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Upload failed: {ex.Message}");
            }

            return null;
        }
    }
}
