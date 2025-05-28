using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gress;
using Gress.Completable;
using WebKit;
using YoutubeDownloader.Core.Downloading;
using YoutubeDownloader.Core.Resolving;
using YoutubeDownloader.Core.Tagging;
using YoutubeDownloader.Framework;
using YoutubeDownloader.Services;
using YoutubeDownloader.Utils;
using YoutubeDownloader.Utils.Extensions;
using YoutubeExplode.Common;
using YoutubeExplode.Exceptions;

namespace YoutubeDownloader.ViewModels.Components;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly ViewModelManager _viewModelManager;
    private readonly SnackbarManager _snackbarManager;
    private readonly DialogManager _dialogManager;
    private readonly SettingsService _settingsService;

    private readonly DisposableCollector _eventRoot = new();
    private readonly ResizableSemaphore _downloadSemaphore = new();
    private readonly AutoResetProgressMuxer _progressMuxer;

    public DashboardViewModel(
        ViewModelManager viewModelManager,
        SnackbarManager snackbarManager,
        DialogManager dialogManager,
        SettingsService settingsService
    )
    {
        _viewModelManager = viewModelManager;
        _snackbarManager = snackbarManager;
        _dialogManager = dialogManager;
        _settingsService = settingsService;

        _progressMuxer = Progress.CreateMuxer().WithAutoReset();

        _eventRoot.Add(
            _settingsService.WatchProperty(
                o => o.ParallelLimit,
                () => _downloadSemaphore.MaxCount = _settingsService.ParallelLimit,
                true
            )
        );

        _eventRoot.Add(
            Progress.WatchProperty(
                o => o.Current,
                () => OnPropertyChanged(nameof(IsProgressIndeterminate))
            )
        );
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProgressIndeterminate))]
    [NotifyCanExecuteChangedFor(nameof(ProcessQueryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowAuthSetupCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowSettingsCommand))]
    [NotifyCanExecuteChangedFor(nameof(GenerateVideoCommand))]
    public partial bool IsBusy { get; set; }

    public ProgressContainer<Percentage> Progress { get; } = new();

    public bool IsProgressIndeterminate => IsBusy && Progress.Current.Fraction is <= 0 or >= 1;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ProcessQueryCommand))]
    public partial string? Query { get; set; }

    [ObservableProperty]
    private string? _workingDirectory;
    public ObservableCollection<DownloadViewModel> ExistingMp3Files { get; } = [];

    public ObservableCollection<DownloadViewModel> Downloads { get; } = [];

    private bool CanShowAuthSetup() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanShowAuthSetup))]
    private async Task ShowAuthSetupAsync() =>
        await _dialogManager.ShowDialogAsync(_viewModelManager.CreateAuthSetupViewModel());

    private bool CanShowSettings() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanShowSettings))]
    private async Task ShowSettingsAsync() =>
        await _dialogManager.ShowDialogAsync(_viewModelManager.CreateSettingsViewModel());

    private async void EnqueueDownload(DownloadViewModel download, int position = 0)
    {
        Downloads.Insert(position, download);
        var progress = _progressMuxer.CreateInput();

        try
        {
            var downloader = new VideoDownloader(_settingsService.LastAuthCookies);
            var tagInjector = new MediaTagInjector();

            using var access = await _downloadSemaphore.AcquireAsync(download.CancellationToken);

            download.Status = DownloadStatus.Started;

            var downloadOption =
                download.DownloadOption
                ?? await downloader.GetBestDownloadOptionAsync(
                    download.Video!.Id,
                    download.DownloadPreference!,
                    _settingsService.ShouldInjectLanguageSpecificAudioStreams,
                    download.CancellationToken
                );

            await downloader.DownloadVideoAsync(
                download.FilePath!,
                download.Video!,
                downloadOption,
                _settingsService.ShouldInjectSubtitles,
                download.Progress.Merge(progress),
                download.CancellationToken
            );

            if (_settingsService.ShouldInjectTags)
            {
                try
                {
                    await tagInjector.InjectTagsAsync(
                        download.FilePath!,
                        download.Video!,
                        download.CancellationToken
                    );
                }
                catch
                {
                    // Media tagging is not critical
                }
            }

            download.Status = DownloadStatus.Completed;
        }
        catch (Exception ex)
        {
            try
            {
                // Delete the incompletely downloaded file
                if (!string.IsNullOrWhiteSpace(download.FilePath))
                    File.Delete(download.FilePath);
            }
            catch
            {
                // Ignore
            }

            download.Status =
                ex is OperationCanceledException ? DownloadStatus.Canceled : DownloadStatus.Failed;

            // Short error message for YouTube-related errors, full for others
            download.ErrorMessage = ex is YoutubeExplodeException ? ex.Message : ex.ToString();
        }
        finally
        {
            progress.ReportCompletion();
            download.Dispose();
        }
    }

    private bool CanProcessQuery() => !IsBusy && !string.IsNullOrWhiteSpace(Query);

    [RelayCommand(CanExecute = nameof(CanProcessQuery))]
    private async Task ProcessQueryAsync()
    {
        if (string.IsNullOrWhiteSpace(Query))
            return;

        IsBusy = true;

        // Small weight so as to not offset any existing download operations
        var progress = _progressMuxer.CreateInput(0.01);

        try
        {
            var resolver = new QueryResolver(_settingsService.LastAuthCookies);
            var downloader = new VideoDownloader(_settingsService.LastAuthCookies);

            // Split queries by newlines
            var queries = Query.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            );

            // Process individual queries
            var queryResults = new List<QueryResult>();
            for (int i = 0; i < queries.Length; i++)
            {
                var query = queries[i]; // Get the current query using the index
                try
                {
                    queryResults.Add(await resolver.ResolveAsync(query));
                }
                // If it's not the only query in the list, don't interrupt the process
                // and report the error via an async notification instead of a sync dialog.
                // https://github.com/Tyrrrz/YoutubeDownloader/issues/563
                catch (YoutubeExplodeException ex)
                    when (ex is VideoUnavailableException or PlaylistUnavailableException
                        && queries.Length > 1
                    )
                {
                    _snackbarManager.Notify(ex.Message);
                }

                progress.Report(Percentage.FromFraction((i + 1.0) / queries.Length));
            }

            // Aggregate results
            var queryResult = QueryResult.Aggregate(queryResults);

            // Single video result
            if (queryResult.Videos.Count == 1)
            {
                var video = queryResult.Videos.Single();

                var downloadOptions = await downloader.GetDownloadOptionsAsync(
                    video.Id,
                    _settingsService.ShouldInjectLanguageSpecificAudioStreams
                );

                var download = await _dialogManager.ShowDialogAsync(
                    _viewModelManager.CreateDownloadSingleSetupViewModel(video, downloadOptions)
                );

                if (download is null)
                    return;

                if (download is not null)
                {
                    // Set file path to be in working directory if set
                    if (!string.IsNullOrEmpty(WorkingDirectory))
                    {
                        var originalFilePath = download.FilePath;
                        if (originalFilePath != null)
                        {
                            var fileName = Path.GetFileName(originalFilePath);
                            download.FilePath = Path.Combine(WorkingDirectory, fileName);
                        }
                    }

                    EnqueueDownload(download);
                    Query = "";
                }

                Query = "";
            }
            // Multiple videos
            else if (queryResult.Videos.Count > 1)
            {
                var downloads = await _dialogManager.ShowDialogAsync(
                    _viewModelManager.CreateDownloadMultipleSetupViewModel(
                        queryResult.Title,
                        queryResult.Videos,
                        // Pre-select videos if they come from a single query and not from search
                        queryResult.Kind
                            is not QueryResultKind.Search
                                and not QueryResultKind.Aggregate
                    )
                );

                if (downloads is null)
                    return;

                foreach (var download in downloads)
                {
                    if (download is not null)
                    {
                        // Set file path to be in working directory if set
                        if (!string.IsNullOrEmpty(WorkingDirectory))
                        {
                            var originalFilePath = download.FilePath;
                            if (originalFilePath != null)
                            {
                                var fileName = Path.GetFileName(originalFilePath);
                                download.FilePath = Path.Combine(WorkingDirectory, fileName);
                            }
                        }

                        EnqueueDownload(download);
                        Query = "";
                    }
                }

                Query = "";
            }
            // No videos found
            else
            {
                await _dialogManager.ShowDialogAsync(
                    _viewModelManager.CreateMessageBoxViewModel(
                        "Nothing found",
                        "Couldn't find any videos based on the query or URL you provided"
                    )
                );
            }
        }
        catch (Exception ex)
        {
            await _dialogManager.ShowDialogAsync(
                _viewModelManager.CreateMessageBoxViewModel(
                    "Error",
                    // Short error message for YouTube-related errors, full for others
                    ex is YoutubeExplodeException
                        ? ex.Message
                        : ex.ToString()
                )
            );
        }
        finally
        {
            progress.ReportCompletion();
            IsBusy = false;
        }
    }

    private void RemoveDownload(DownloadViewModel download)
    {
        Downloads.Remove(download);
        download.CancelCommand.Execute(null);
        download.Dispose();
    }

    [RelayCommand]
    private void RemoveSuccessfulDownloads()
    {
        foreach (var download in Downloads.ToArray())
        {
            if (download.Status == DownloadStatus.Completed)
                RemoveDownload(download);
        }
    }

    [RelayCommand]
    private void RemoveInactiveDownloads()
    {
        foreach (var download in Downloads.ToArray())
        {
            if (
                download.Status
                is DownloadStatus.Completed
                    or DownloadStatus.Failed
                    or DownloadStatus.Canceled
            )
                RemoveDownload(download);
        }
    }

    [RelayCommand]
    private void RestartDownload(DownloadViewModel download)
    {
        var position = Math.Max(0, Downloads.IndexOf(download));
        RemoveDownload(download);

        var newDownload = download.DownloadOption is not null
            ? _viewModelManager.CreateDownloadViewModel(
                download.Video!,
                download.DownloadOption,
                download.FilePath!
            )
            : _viewModelManager.CreateDownloadViewModel(
                download.Video!,
                download.DownloadPreference!,
                download.FilePath!
            );

        EnqueueDownload(newDownload, position);
    }

    [RelayCommand]
    private void RestartFailedDownloads()
    {
        foreach (var download in Downloads.ToArray())
        {
            if (download.Status == DownloadStatus.Failed)
                RestartDownload(download);
        }
    }

    [RelayCommand]
    private void CancelAllDownloads()
    {
        foreach (var download in Downloads)
            download.CancelCommand.Execute(null);
    }

    #region video generation


    // Method to prompt for working directory on load
    public async Task PromptForWorkingDirectoryAsync()
    {
        try
        {
            string? selectedDirectory = null;

            // If we have a saved directory and it exists, use it without prompting
            if (
                !string.IsNullOrEmpty(_settingsService.LastWorkingDirectory)
                && Directory.Exists(_settingsService.LastWorkingDirectory)
            )
            {
                selectedDirectory = _settingsService.LastWorkingDirectory;
            }
            else
            {
                // Create and show the working directory dialog
                var workingDirDialog = _viewModelManager.CreateWorkingDirectoryDialogViewModel(
                    initialDirectory: _settingsService.LastWorkingDirectory
                );

                selectedDirectory = await _dialogManager.ShowDialogAsync(workingDirDialog);
            }

            if (!string.IsNullOrEmpty(selectedDirectory))
            {
                WorkingDirectory = selectedDirectory;
                _settingsService.LastWorkingDirectory = selectedDirectory;
                _settingsService.Save();

                // Scan for existing MP3 files
                await ScanForExistingMp3FilesAsync();
            }
            else
            {
                // If no directory was selected, use a default
                WorkingDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                    "YouTubeDownloader"
                );

                // Create directory if it doesn't exist
                if (!Directory.Exists(WorkingDirectory))
                    Directory.CreateDirectory(WorkingDirectory);

                _settingsService.LastWorkingDirectory = WorkingDirectory;
                _settingsService.Save();

                await ScanForExistingMp3FilesAsync();
            }
        }
        catch (Exception ex)
        {
            _snackbarManager.Notify($"Error setting working directory: {ex.Message}");
        }
    }

    // Method to scan for existing MP3 files in the working directory
    private async Task ScanForExistingMp3FilesAsync()
    {
        if (string.IsNullOrEmpty(WorkingDirectory) || !Directory.Exists(WorkingDirectory))
            return;

        try
        {
            // Clear existing items
            ExistingMp3Files.Clear();

            // Find all MP3 files
            var mp3Files = Directory.GetFiles(WorkingDirectory, "*.mp3");

            if (mp3Files.Length > 0)
            {
                IsBusy = true;
                var progress = _progressMuxer.CreateInput(0.01);

                for (int i = 0; i < mp3Files.Length; i++)
                {
                    var filePath = mp3Files[i];
                    try
                    {
                        // Create a dummy video object to represent this file
                        var fileName = Path.GetFileName(filePath);

                        // Create author
                        var author = new YoutubeExplode.Common.Author(
                            channelId: new YoutubeExplode.Channels.ChannelId("local_file_channel"), // Create a valid dummy ChannelId
                            channelTitle: "Local File"
                        );

                        // Create thumbnails
                        var thumbnails = new[]
                        {
                            new YoutubeExplode.Common.Thumbnail(
                                url: "https://via.placeholder.com/120",
                                resolution: new YoutubeExplode.Common.Resolution(
                                    width: 120,
                                    height: 90
                                )
                            ),
                        };

                        // Create engagement stats
                        var engagement = new YoutubeExplode.Videos.Engagement(
                            viewCount: 0,
                            likeCount: 0,
                            dislikeCount: 0
                        );

                        // Get file duration
                        TimeSpan? duration = null;
                        double durationSeconds = 0;
                        try
                        {
                            var tagFile = TagLib.File.Create(filePath);
                            duration = tagFile.Properties.Duration;
                            durationSeconds = duration.Value.TotalSeconds;
                        }
                        catch
                        {
                            // If we can't read tags, just continue with zero duration
                        }

                        // Create video with the correct constructor parameters
                        var video = new YoutubeExplode.Videos.Video(
                            id: new YoutubeExplode.Videos.VideoId("existing_" + i),
                            title: fileName,
                            author: author,
                            uploadDate: DateTimeOffset.Now,
                            description: "Existing MP3 file",
                            duration: duration,
                            thumbnails: thumbnails,
                            keywords: Array.Empty<string>(),
                            engagement: engagement
                        );

                        // Create download preference with the correct constructor
                        var downloadPreference = new VideoDownloadPreference(
                            PreferredContainer: YoutubeExplode.Videos.Streams.Container.Mp3,
                            PreferredVideoQuality: VideoQualityPreference.Highest
                        );

                        // Create a download view model for this file
                        var downloadViewModel = _viewModelManager.CreateDownloadViewModel(
                            video,
                            downloadPreference,
                            filePath
                        );

                        // Mark as completed since it already exists
                        downloadViewModel.Status = DownloadStatus.Completed;

                        // Set the duration
                        downloadViewModel.Duration = durationSeconds;

                        // Add to our collection
                        ExistingMp3Files.Add(downloadViewModel);

                        // Add a small delay to ensure UI responsiveness during scanning
                        await Task.Delay(10);
                    }
                    catch (Exception ex)
                    {
                        // Log exception and skip files that can't be processed
                        System.Diagnostics.Debug.WriteLine(
                            $"Error processing file {filePath}: {ex.Message}"
                        );
                    }

                    progress.Report(Percentage.FromFraction((i + 1.0) / mp3Files.Length));
                }

                progress.ReportCompletion();
                IsBusy = false;

                // Notify user
                if (ExistingMp3Files.Count > 0)
                {
                    _snackbarManager.Notify(
                        $"Found {ExistingMp3Files.Count} existing MP3 files in the working directory."
                    );
                }
            }
        }
        catch (Exception ex)
        {
            _snackbarManager.Notify($"Error scanning for MP3 files: {ex.Message}");
        }
    }

    // ADDED: GenerateVideoCommand (empty for now)
    private bool CanGenerateVideo() => !IsBusy && Downloads.Any(); // Example: enable if not busy and there are downloads

    [RelayCommand(CanExecute = nameof(CanGenerateVideo))]
    private void GenerateVideo()
    {
        // Get all completed downloads from regular downloads
        var completedDownloads = Downloads
            .Where(d => d.Status == DownloadStatus.Completed)
            .ToList();

        // Combine with existing MP3 files
        var allMp3Files = completedDownloads
            .Concat(ExistingMp3Files)
            .Where(d => Path.GetExtension(d.FilePath)?.ToLowerInvariant() == ".mp3")
            .ToList();

        System.Diagnostics.Debug.WriteLine($"Found {allMp3Files.Count} total MP3 files:");

        // List each MP3 file path and duration
        foreach (var download in allMp3Files)
        {
            System.Diagnostics.Debug.WriteLine(
                $"- {download.FilePath} (Duration: {download.Duration} seconds)"
            );
        }

        // If no MP3 files were found, show a notification
        if (!allMp3Files.Any())
        {
            _snackbarManager.Notify("No MP3 files found.");
            return;
        }

        // For now, just notify that files were found
        _snackbarManager.Notify(
            $"Found {allMp3Files.Count} MP3 files. Check debug output for details."
        );

        // Your future implementation will go here
    }
    #endregion



    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CancelAllDownloads();

            _eventRoot.Dispose();
            _downloadSemaphore.Dispose();
        }

        base.Dispose(disposing);
    }

    // Helper class to compare download view models by file path
    private class DownloadViewModelFilePathComparer : IEqualityComparer<DownloadViewModel>
    {
        public bool Equals(DownloadViewModel? x, DownloadViewModel? y)
        {
            if (x == null || y == null)
                return false;

            return string.Equals(x.FilePath, y.FilePath, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(DownloadViewModel obj)
        {
            if (obj == null || obj.FilePath == null)
                return 0;

            return obj.FilePath.ToLowerInvariant().GetHashCode();
        }
    }
}
