using System;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AvaloniaWebView;

namespace YoutubeDownloader.Utils;

// Interface for our custom image loader
public interface IResourceImageLoader
{
    Task<Bitmap?> LoadImageAsync(string url);
}

// Implementation that can load from both web and resources
public class ResourceAwareImageLoader : IResourceImageLoader
{
    public async Task<Bitmap?> LoadImageAsync(string url)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        // Handle avares:// URLs
        if (url.StartsWith("avares://"))
        {
            try
            {
                var uri = new Uri(url);
                using var stream = AssetLoader.Open(uri); // Use Avalonia's built-in AssetLoader
                if (stream != null)
                {
                    return new Bitmap(stream);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load resource: {ex.Message}");
            }
            return null;
        }

        // For web URLs, use HttpClient
        try
        {
            using var client = new System.Net.Http.HttpClient();
            var response = await client.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                using var stream = await response.Content.ReadAsStreamAsync();
                return new Bitmap(stream);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load image from URL: {ex.Message}");
        }

        return null;
    }
}
