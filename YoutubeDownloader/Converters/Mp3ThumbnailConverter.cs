using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using YoutubeExplode.Videos;

namespace YoutubeDownloader.Converters;

public class Mp3ThumbnailConverter : IValueConverter
{
    // Singleton instance for easy access in XAML
    public static Mp3ThumbnailConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not IVideo video)
            return null;

        // Check if it's a local MP3 file (using our dummy video ID prefix)
        if (video.Id.ToString().StartsWith("existing_"))
        {
            // For local MP3 files, return a local resource path
            return "avares://YoutubeDownloader/Assets/mp3.png";
        }

        // For YouTube videos, try to get the thumbnail URL
        if (video.Thumbnails.Count > 0)
        {
            return video.Thumbnails[0].Url;
        }

        return null;
    }

    public object? ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture
    )
    {
        throw new NotImplementedException();
    }
}
