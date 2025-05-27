using System;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using YoutubeDownloader.Framework;
using YoutubeDownloader.ViewModels.Components;

namespace YoutubeDownloader.ViewModels.Dialogs;

public partial class MediaPlayerViewModel : DialogViewModelBase<double?>
{
    private readonly LibVLC _libVLC;
    private readonly MediaPlayer _mediaPlayer;
    private readonly DispatcherTimer _timer;
    private readonly string _filePath;
    private readonly DownloadViewModel _downloadViewModel;

    [ObservableProperty]
    public partial string Title { get; set; } = "";

    [ObservableProperty]
    public partial double Duration { get; set; }

    [ObservableProperty]
    public partial double CurrentPosition { get; set; }

    [ObservableProperty]
    public partial bool IsPlaying { get; set; }

    [ObservableProperty]
    public partial string CurrentTimeText { get; set; } = "00:00";

    [ObservableProperty]
    public partial string DurationText { get; set; } = "00:00";

    public MediaPlayerViewModel(string filePath, DownloadViewModel downloadViewModel)
    {
        _filePath = filePath;
        _downloadViewModel = downloadViewModel;

        // Initialize LibVLC
        _libVLC = new LibVLC();
        _mediaPlayer = new MediaPlayer(_libVLC);

        // Set up timer for position updates
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += Timer_Tick;

        // Set initial duration from download view model
        Duration = downloadViewModel.Duration ?? 30; // Default to 30 seconds if not set
        Title = downloadViewModel.Video?.Title ?? "Unknown Title";

        // Initialize the player
        InitializePlayer();
    }

    private void InitializePlayer()
    {
        var media = new Media(_libVLC, _filePath, FromType.FromPath);
        _mediaPlayer.Media = media;

        _mediaPlayer.EndReached += (sender, args) =>
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsPlaying = false;
                CurrentPosition = 0;
                _timer.Stop();
            });
        };

        _mediaPlayer.TimeChanged += (sender, args) =>
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                var currentSeconds = _mediaPlayer.Time / 1000.0;

                // Stop if we've reached the set duration
                if (currentSeconds >= Duration)
                {
                    _mediaPlayer.Stop();
                    IsPlaying = false;
                    _timer.Stop();
                    CurrentPosition = Duration;
                    UpdateTimeDisplays();
                }
            });
        };

        UpdateTimeDisplays();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_mediaPlayer.IsPlaying)
        {
            var currentSeconds = _mediaPlayer.Time / 1000.0;
            CurrentPosition = Math.Min(currentSeconds, Duration);
            UpdateTimeDisplays();
        }
    }

    private void UpdateTimeDisplays()
    {
        CurrentTimeText = FormatTime(CurrentPosition);
        DurationText = FormatTime(Duration);
    }

    private string FormatTime(double seconds)
    {
        var timeSpan = TimeSpan.FromSeconds(seconds);
        return timeSpan.ToString(@"mm\:ss");
    }

    [RelayCommand]
    private void PlayPause()
    {
        if (IsPlaying)
        {
            _mediaPlayer.Pause();
            _timer.Stop();
        }
        else
        {
            // If at the end, restart from beginning
            if (CurrentPosition >= Duration)
            {
                CurrentPosition = 0;
                _mediaPlayer.Time = 0;
            }

            _mediaPlayer.Play();
            _timer.Start();
        }

        IsPlaying = !IsPlaying;
    }

    [RelayCommand]
    private void Seek(double position)
    {
        CurrentPosition = Math.Min(position, Duration);
        _mediaPlayer.Time = (long)(CurrentPosition * 1000);
        UpdateTimeDisplays();
    }

    partial void OnDurationChanged(double value)
    {
        // Ensure current position doesn't exceed duration
        if (CurrentPosition > value)
        {
            CurrentPosition = value;
            _mediaPlayer.Time = (long)(value * 1000);
        }

        UpdateTimeDisplays();
    }

    [RelayCommand]
    private void ApplyDuration()
    {
        // Update the duration in the download view model
        _downloadViewModel.Duration = Duration;

        // Close the dialog and return the duration
        Close(Duration);
    }

    [RelayCommand]
    private void Cancel()
    {
        Close(null);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer?.Stop();
            _mediaPlayer?.Stop();
            _mediaPlayer?.Dispose();
            _libVLC?.Dispose();
        }

        base.Dispose(disposing);
    }
}
