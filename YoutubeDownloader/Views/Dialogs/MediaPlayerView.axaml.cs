using Avalonia.Controls;
using Avalonia.Input;
using YoutubeDownloader.Framework;
using YoutubeDownloader.ViewModels.Dialogs;

namespace YoutubeDownloader.Views.Dialogs;

public partial class MediaPlayerView : UserControl<MediaPlayerViewModel>
{
    public MediaPlayerView() => InitializeComponent();

    private void PositionSlider_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Slider slider)
        {
            DataContext.SeekCommand.Execute(slider.Value);
        }
    }
}
