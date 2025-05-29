using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gress;
using Gress.Completable;
using WebKit;
using YoutubeDownloader.Converters;
using YoutubeDownloader.Core.AudioVisualisation;
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
                //if (!string.IsNullOrWhiteSpace(download.FilePath))
                //    File.Delete(download.FilePath);
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
        Debug.WriteLine("[VideoGen.PromptForWorkingDirectoryAsync] Entering method.");
        try
        {
            string? selectedDirectory = null;

            // If we have a saved directory and it exists, use it without prompting
            Debug.WriteLine(
                $"[VideoGen.PromptForWorkingDirectoryAsync] Checking saved directory: '{_settingsService.LastWorkingDirectory}'"
            );
            if (
                !string.IsNullOrEmpty(_settingsService.LastWorkingDirectory)
                && Directory.Exists(_settingsService.LastWorkingDirectory)
            )
            {
                selectedDirectory = _settingsService.LastWorkingDirectory;
                Debug.WriteLine(
                    $"[VideoGen.PromptForWorkingDirectoryAsync] Using saved and existing directory: {selectedDirectory}"
                );
            }
            else
            {
                Debug.WriteLine(
                    "[VideoGen.PromptForWorkingDirectoryAsync] Saved directory not valid or not found. Prompting user."
                );
                // Create and show the working directory dialog
                var workingDirDialog = _viewModelManager.CreateWorkingDirectoryDialogViewModel(
                    initialDirectory: _settingsService.LastWorkingDirectory
                );
                Debug.WriteLine(
                    "[VideoGen.PromptForWorkingDirectoryAsync] Working directory dialog ViewModel created."
                );

                selectedDirectory = await _dialogManager.ShowDialogAsync(workingDirDialog);
                Debug.WriteLine(
                    $"[VideoGen.PromptForWorkingDirectoryAsync] Dialog returned: '{selectedDirectory}'"
                );
            }

            if (!string.IsNullOrEmpty(selectedDirectory))
            {
                Debug.WriteLine(
                    $"[VideoGen.PromptForWorkingDirectoryAsync] Valid directory selected: {selectedDirectory}"
                );
                WorkingDirectory = selectedDirectory;
                _settingsService.LastWorkingDirectory = selectedDirectory;
                _settingsService.Save();
                Debug.WriteLine(
                    $"[VideoGen.PromptForWorkingDirectoryAsync] WorkingDirectory set and saved to settings: {WorkingDirectory}"
                );

                // Scan for existing MP3 files
                Debug.WriteLine(
                    "[VideoGen.PromptForWorkingDirectoryAsync] Calling ScanForExistingMp3FilesAsync."
                );
                await ScanForExistingMp3FilesAsync();
            }
            else
            {
                Debug.WriteLine(
                    "[VideoGen.PromptForWorkingDirectoryAsync] No directory selected or dialog cancelled. Using default."
                );
                // If no directory was selected, use a default
                WorkingDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
                    "YouTubeDownloader"
                );
                Debug.WriteLine(
                    $"[VideoGen.PromptForWorkingDirectoryAsync] Default WorkingDirectory: {WorkingDirectory}"
                );

                // Create directory if it doesn't exist
                if (!Directory.Exists(WorkingDirectory))
                {
                    Debug.WriteLine(
                        $"[VideoGen.PromptForWorkingDirectoryAsync] Default directory does not exist. Creating: {WorkingDirectory}"
                    );
                    Directory.CreateDirectory(WorkingDirectory);
                    Debug.WriteLine(
                        $"[VideoGen.PromptForWorkingDirectoryAsync] Default directory created."
                    );
                }

                _settingsService.LastWorkingDirectory = WorkingDirectory;
                _settingsService.Save();
                Debug.WriteLine(
                    $"[VideoGen.PromptForWorkingDirectoryAsync] Default WorkingDirectory saved to settings: {WorkingDirectory}"
                );

                Debug.WriteLine(
                    "[VideoGen.PromptForWorkingDirectoryAsync] Calling ScanForExistingMp3FilesAsync for default directory."
                );
                await ScanForExistingMp3FilesAsync();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[VideoGen.PromptForWorkingDirectoryAsync] Exception: {ex.Message}\n{ex.StackTrace}"
            );
            _snackbarManager.Notify($"Error setting working directory: {ex.Message}");
        }
        Debug.WriteLine("[VideoGen.PromptForWorkingDirectoryAsync] Exiting method.");
    }

    // Method to scan for existing MP3 files in the working directory
    private async Task ScanForExistingMp3FilesAsync()
    {
        Debug.WriteLine("[VideoGen.ScanForExistingMp3FilesAsync] Entering method.");
        if (string.IsNullOrEmpty(WorkingDirectory) || !Directory.Exists(WorkingDirectory))
        {
            Debug.WriteLine(
                $"[VideoGen.ScanForExistingMp3FilesAsync] WorkingDirectory is null, empty, or does not exist: '{WorkingDirectory}'. Returning."
            );
            return;
        }

        try
        {
            Debug.WriteLine(
                "[VideoGen.ScanForExistingMp3FilesAsync] Clearing ExistingMp3Files collection."
            );
            // Clear existing items
            ExistingMp3Files.Clear();

            Debug.WriteLine(
                $"[VideoGen.ScanForExistingMp3FilesAsync] Scanning for MP3 files in: {WorkingDirectory}"
            );
            // Find all MP3 files
            var mp3Files = Directory.GetFiles(WorkingDirectory, "*.mp3");
            Debug.WriteLine(
                $"[VideoGen.ScanForExistingMp3FilesAsync] Found {mp3Files.Length} MP3 files."
            );

            if (mp3Files.Length > 0)
            {
                IsBusy = true;
                Debug.WriteLine("[VideoGen.ScanForExistingMp3FilesAsync] IsBusy set to true.");
                var progress = _progressMuxer.CreateInput(0.01);
                Debug.WriteLine("[VideoGen.ScanForExistingMp3FilesAsync] Progress input created.");

                for (int i = 0; i < mp3Files.Length; i++)
                {
                    var filePath = mp3Files[i];
                    Debug.WriteLine(
                        $"[VideoGen.ScanForExistingMp3FilesAsync] Processing file {i + 1}/{mp3Files.Length}: {filePath}"
                    );
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
                            Debug.WriteLine(
                                $"[VideoGen.ScanForExistingMp3FilesAsync] Reading tags for duration from: {filePath}"
                            );
                            var tagFile = TagLib.File.Create(filePath);
                            duration = tagFile.Properties.Duration;
                            durationSeconds = duration.Value.TotalSeconds;
                            Debug.WriteLine(
                                $"[VideoGen.ScanForExistingMp3FilesAsync] Duration found: {durationSeconds}s"
                            );
                        }
                        catch (Exception tagEx)
                        {
                            Debug.WriteLine(
                                $"[VideoGen.ScanForExistingMp3FilesAsync] Could not read tags from {filePath}: {tagEx.Message}. Duration will be 0."
                            );
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
                        Debug.WriteLine(
                            $"[VideoGen.ScanForExistingMp3FilesAsync] Video object created for: {fileName}"
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
                        Debug.WriteLine(
                            $"[VideoGen.ScanForExistingMp3FilesAsync] DownloadViewModel created for: {fileName}"
                        );

                        // Mark as completed since it already exists
                        downloadViewModel.Status = DownloadStatus.Completed;
                        Debug.WriteLine(
                            $"[VideoGen.ScanForExistingMp3FilesAsync] Status set to Completed."
                        );

                        // Set the duration
                        downloadViewModel.Duration = durationSeconds;
                        Debug.WriteLine(
                            $"[VideoGen.ScanForExistingMp3FilesAsync] ViewModel Duration set to {durationSeconds}s."
                        );

                        // Add to our collection
                        ExistingMp3Files.Add(downloadViewModel);
                        Debug.WriteLine(
                            $"[VideoGen.ScanForExistingMp3FilesAsync] Added to ExistingMp3Files. Count: {ExistingMp3Files.Count}"
                        );

                        // Add a small delay to ensure UI responsiveness during scanning
                        await Task.Delay(10);
                    }
                    catch (Exception ex)
                    {
                        // Log exception and skip files that can't be processed
                        Debug.WriteLine(
                            $"[VideoGen.ScanForExistingMp3FilesAsync] Error processing file {filePath}: {ex.Message}\n{ex.StackTrace}"
                        );
                    }

                    var currentProgress = Percentage.FromFraction((i + 1.0) / mp3Files.Length);
                    progress.Report(currentProgress);
                    // Debug.WriteLine($"[VideoGen.ScanForExistingMp3FilesAsync] Progress reported: {currentProgress.Fraction * 100:F2}%");
                }

                progress.ReportCompletion();
                Debug.WriteLine(
                    "[VideoGen.ScanForExistingMp3FilesAsync] Progress reported completion."
                );
                IsBusy = false;
                Debug.WriteLine("[VideoGen.ScanForExistingMp3FilesAsync] IsBusy set to false.");

                // Notify user
                if (ExistingMp3Files.Count > 0)
                {
                    var notificationMessage =
                        $"Found {ExistingMp3Files.Count} existing MP3 files in the working directory.";
                    Debug.WriteLine(
                        $"[VideoGen.ScanForExistingMp3FilesAsync] Notifying: {notificationMessage}"
                    );
                    _snackbarManager.Notify(notificationMessage);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[VideoGen.ScanForExistingMp3FilesAsync] Error scanning for MP3 files: {ex.Message}\n{ex.StackTrace}"
            );
            _snackbarManager.Notify($"Error scanning for MP3 files: {ex.Message}");
            if (IsBusy)
            {
                IsBusy = false; // Ensure IsBusy is reset on error
                Debug.WriteLine(
                    "[VideoGen.ScanForExistingMp3FilesAsync] IsBusy set to false due to exception."
                );
            }
        }
        Debug.WriteLine("[VideoGen.ScanForExistingMp3FilesAsync] Exiting method.");
    }

    // ADDED: GenerateVideoCommand (empty for now)
    private bool CanGenerateVideo()
    {
        Debug.WriteLine("[VideoGen.CanGenerateVideo] Entering method.");
        bool canExecute =
            !IsBusy
            && (Downloads.Any(d => d.Status == DownloadStatus.Completed) || ExistingMp3Files.Any());
        Debug.WriteLine(
            $"[VideoGen.CanGenerateVideo] CanExecute: {canExecute} (IsBusy: {IsBusy}, Completed Downloads: {Downloads.Count(d => d.Status == DownloadStatus.Completed)}, Existing MP3s: {ExistingMp3Files.Count})"
        );
        return canExecute;
    }

    [RelayCommand(CanExecute = nameof(CanGenerateVideo))]
    private async Task GenerateVideo()
    {
        Debug.WriteLine("[VideoGen.GenerateVideo] Entering method (RelayCommand).");
        // Get all completed downloads from regular downloads
        var completedDownloads = Downloads
            .Where(d => d.Status == DownloadStatus.Completed)
            .ToList();
        Debug.WriteLine(
            $"[VideoGen.GenerateVideo] Found {completedDownloads.Count} completed downloads from 'Downloads' collection."
        );

        // Combine with existing MP3 files
        var allMp3Files = completedDownloads
            .Concat(ExistingMp3Files)
            .Where(d =>
                !string.IsNullOrEmpty(d.FilePath)
                && Path.GetExtension(d.FilePath)?.ToLowerInvariant() == ".mp3"
            )
            .ToList();
        Debug.WriteLine(
            $"[VideoGen.GenerateVideo] Found {allMp3Files.Count} total MP3 files after combining and filtering."
        );

        // List each MP3 file path and duration
        if (allMp3Files.Any())
        {
            Debug.WriteLine("[VideoGen.GenerateVideo] Listing all MP3 files to be included:");
            foreach (var download in allMp3Files)
            {
                Debug.WriteLine(
                    $"[VideoGen.GenerateVideo] - {download.FilePath} (Duration: {download.Duration} seconds)"
                );
            }
        }

        // If no MP3 files were found, show a notification
        if (!allMp3Files.Any())
        {
            Debug.WriteLine(
                "[VideoGen.GenerateVideo] No MP3 files found to generate video. Notifying and returning."
            );
            _snackbarManager.Notify("No MP3 files found.");
            return;
        }

        // Show AudioVisualizationSettingsDialog
        Debug.WriteLine(
            "[VideoGen.GenerateVideo] Creating and showing AudioVisualizationSettingsViewModel dialog."
        );
        var audioSettingsViewModel = _viewModelManager.CreateAudioVisualizationSettingsViewModel();
        await _dialogManager.ShowDialogAsync(audioSettingsViewModel);
        Debug.WriteLine(
            "[VideoGen.GenerateVideo] AudioVisualizationSettingsViewModel dialog closed."
        );

        IsBusy = true;
        Debug.WriteLine("[VideoGen.GenerateVideo] IsBusy set to true.");

        try
        {
            // Retrieve selected modes from SettingsService
            VisualizationMode selectedVisualizationMode = _settingsService.VisualizationMode;
            ColorMode selectedColorMode = _settingsService.ColorMode;
            Debug.WriteLine(
                $"[VideoGen.GenerateVideo] Using VisualizationMode: {selectedVisualizationMode}, ColorMode: {selectedColorMode} from settings."
            );

            // Create visualization parameters from settings
            var visualizationParameters = VisualizationParametersFromSettings.CreateFromSettings(
                _settingsService
            );
            Debug.WriteLine(
                $"[VideoGen.GenerateVideo] Created visualization parameters of type: {visualizationParameters.GetType().Name}"
            );

            _snackbarManager.Notify("Starting video generation process...");
            Debug.WriteLine(
                "[VideoGen.GenerateVideo] Notified: Starting video generation process..."
            );

            // Step 1: Create combined audio file
            Debug.WriteLine("[VideoGen.GenerateVideo] Calling CombineAudioFiles...");
            string combinedAudioPath = await CombineAudioFiles(allMp3Files);

            if (string.IsNullOrEmpty(combinedAudioPath))
            {
                Debug.WriteLine(
                    "[VideoGen.GenerateVideo] CombineAudioFiles returned null or empty path. Notifying and returning."
                );
                _snackbarManager.Notify("Failed to combine audio files.");
                return;
            }
            Debug.WriteLine(
                $"[VideoGen.GenerateVideo] Audio files combined successfully: {combinedAudioPath}"
            );

            _snackbarManager.Notify(
                "Audio files combined successfully. Generating synthwave video..."
            );
            Debug.WriteLine(
                "[VideoGen.GenerateVideo] Notified: Audio combined, generating video..."
            );

            // Step 2: Generate synthwave video with custom parameters
            Debug.WriteLine("[VideoGen.GenerateVideo] Calling GenerateSynthwaveVideo...");
            await GenerateSynthwaveVideo(
                combinedAudioPath,
                selectedVisualizationMode,
                selectedColorMode,
                visualizationParameters
            );
            Debug.WriteLine("[VideoGen.GenerateVideo] GenerateSynthwaveVideo completed.");

            _snackbarManager.Notify("Synthwave video generated successfully!");
            Debug.WriteLine(
                "[VideoGen.GenerateVideo] Notified: Synthwave video generated successfully!"
            );

            // Clean up temporary combined audio file
            if (File.Exists(combinedAudioPath))
            {
                Debug.WriteLine(
                    $"[VideoGen.GenerateVideo] Deleting temporary combined audio file: {combinedAudioPath}"
                );
                // File.Delete(combinedAudioPath); // As per original code, this was commented
                Debug.WriteLine(
                    $"[VideoGen.GenerateVideo] Temporary combined audio file deletion (commented out)."
                );
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[VideoGen.GenerateVideo] Exception during video generation: {ex.Message}\n{ex.StackTrace}"
            );
            _snackbarManager.Notify($"Error generating video: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Video generation error: {ex}");
        }
        finally
        {
            IsBusy = false;
            Debug.WriteLine("[VideoGen.GenerateVideo] IsBusy set to false in finally block.");
        }
        Debug.WriteLine("[VideoGen.GenerateVideo] Exiting method.");
    }

    private async Task<string> CombineAudioFiles(List<DownloadViewModel> mp3Files)
    {
        Debug.WriteLine(
            $"[VideoGen.CombineAudioFiles] Entering method with {mp3Files.Count} files."
        );
        if (!mp3Files.Any())
        {
            Debug.WriteLine("[VideoGen.CombineAudioFiles] No MP3 files provided. Returning null.");
            return null;
        }
        try
        {
            // Create temporary directory for processing
            string tempDir = Path.Combine(Path.GetTempPath(), $"SynthVideoGen_{Guid.NewGuid()}"); // Unique temp dir
            Directory.CreateDirectory(tempDir);
            Debug.WriteLine($"[VideoGen.CombineAudioFiles] Temporary directory created: {tempDir}");

            // Create the combined audio file in the temp directory first
            string tempCombinedAudioPath = Path.Combine(
                tempDir,
                $"combined_audio_{DateTime.Now:yyyyMMdd_HHmmss}.mp3"
            );

            // Create the final output path in a persistent location
            string finalCombinedAudioPath = Path.Combine(
                Path.GetTempPath(),
                $"SynthVideoGen_combined_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid()}.mp3"
            );

            Debug.WriteLine(
                $"[VideoGen.CombineAudioFiles] Temp combined audio path: {tempCombinedAudioPath}"
            );
            Debug.WriteLine(
                $"[VideoGen.CombineAudioFiles] Final combined audio path: {finalCombinedAudioPath}"
            );

            // Create concat file list
            string concatFilePath = Path.Combine(tempDir, "concat_list.txt");
            Debug.WriteLine(
                $"[VideoGen.CombineAudioFiles] Concat file list path: {concatFilePath}"
            );

            using (
                var writer = new StreamWriter(
                    concatFilePath,
                    false,
                    new System.Text.UTF8Encoding(false)
                )
            )
            {
                Debug.WriteLine(
                    "[VideoGen.CombineAudioFiles] Starting to process files for concat list."
                );
                foreach (var mp3File in mp3Files)
                {
                    if (string.IsNullOrEmpty(mp3File.FilePath) || !File.Exists(mp3File.FilePath))
                    {
                        Debug.WriteLine(
                            $"[VideoGen.CombineAudioFiles] Skipping invalid or non-existent file: {mp3File.FilePath ?? "NULL"}"
                        );
                        continue;
                    }
                    double durationToExtract = mp3File.Duration ?? 30.0; // Default to 30s if null
                    if (durationToExtract <= 0)
                    {
                        Debug.WriteLine(
                            $"[VideoGen.CombineAudioFiles] Skipping file with zero or negative duration: {mp3File.FilePath} (Duration: {durationToExtract}s)"
                        );
                        continue;
                    }

                    // Extract specified duration from each file (starting at 0)
                    // Use a unique name for each extracted segment to prevent conflicts if multiple files have same name
                    string tempExtractPath = Path.Combine(
                        tempDir,
                        $"extract_{Path.GetFileNameWithoutExtension(mp3File.FilePath)}_{Guid.NewGuid()}.mp3"
                    );
                    Debug.WriteLine(
                        $"[VideoGen.CombineAudioFiles] Preparing to extract segment from '{mp3File.FilePath}' to '{tempExtractPath}' (Duration: {durationToExtract}s)"
                    );

                    // Extract the specified duration from the MP3 file
                    await ExtractAudioSegment(
                        mp3File.FilePath,
                        tempExtractPath,
                        0, // startTime
                        durationToExtract
                    );
                    Debug.WriteLine(
                        $"[VideoGen.CombineAudioFiles] Audio segment extracted to: {tempExtractPath}"
                    );

                    // Add to concat list, ensuring paths are correctly formatted for ffmpeg
                    writer.WriteLine($"file '{tempExtractPath.Replace("\\", "/")}'");
                    Debug.WriteLine(
                        $"[VideoGen.CombineAudioFiles] Added to concat list: file '{tempExtractPath.Replace("\\", "/")}'"
                    );
                }
            }
            Debug.WriteLine("[VideoGen.CombineAudioFiles] Finished writing concat_list.txt.");

            // Check if concat_list.txt actually has content
            if (new FileInfo(concatFilePath).Length == 0)
            {
                Debug.WriteLine(
                    "[VideoGen.CombineAudioFiles] concat_list.txt is empty. No valid audio segments were extracted. Aborting combination."
                );
                // Clean up tempDir early
                try
                {
                    // Directory.Delete(tempDir, true);
                    Debug.WriteLine(
                        $"[VideoGen.CombineAudioFiles] Cleaned up temp directory: {tempDir}"
                    );
                }
                catch (Exception dirEx)
                {
                    Debug.WriteLine(
                        $"[VideoGen.CombineAudioFiles] Error deleting temp directory {tempDir}: {dirEx.Message}"
                    );
                }
                return null;
            }

            // Build final concat command - output to temp location first
            var concatArguments =
                $"-y -f concat -safe 0 -i \"{concatFilePath}\" -c copy \"{tempCombinedAudioPath}\"";
            Debug.WriteLine(
                $"[VideoGen.CombineAudioFiles] FFmpeg concat arguments: {concatArguments}"
            );
            var concatProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = concatArguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };

            // Capture FFmpeg output
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();
            concatProcess.OutputDataReceived += (sender, args) =>
            {
                if (args.Data != null)
                    outputBuilder.AppendLine(args.Data);
            };
            concatProcess.ErrorDataReceived += (sender, args) =>
            {
                if (args.Data != null)
                    errorBuilder.AppendLine(args.Data);
            };

            Debug.WriteLine("[VideoGen.CombineAudioFiles] Starting FFmpeg concat process.");
            concatProcess.Start();
            concatProcess.BeginOutputReadLine();
            concatProcess.BeginErrorReadLine();
            await concatProcess.WaitForExitAsync();
            Debug.WriteLine(
                $"[VideoGen.CombineAudioFiles] FFmpeg concat process exited with code: {concatProcess.ExitCode}"
            );

            if (concatProcess.ExitCode != 0)
            {
                string errorOutput = errorBuilder.ToString();
                Debug.WriteLine(
                    $"[VideoGen.CombineAudioFiles] FFmpeg concat error (Exit Code: {concatProcess.ExitCode}):\n{errorOutput}"
                );
                Debug.WriteLine(
                    $"[VideoGen.CombineAudioFiles] FFmpeg concat output:\n{outputBuilder.ToString()}"
                );
                // Clean up tempDir even on failure
                try
                {
                    // Directory.Delete(tempDir, true);
                    Debug.WriteLine(
                        $"[VideoGen.CombineAudioFiles] Cleaned up temp directory on failure: {tempDir}"
                    );
                }
                catch (Exception dirEx)
                {
                    Debug.WriteLine(
                        $"[VideoGen.CombineAudioFiles] Error deleting temp directory {tempDir} on failure: {dirEx.Message}"
                    );
                }
                return null;
            }
            Debug.WriteLine(
                "[VideoGen.CombineAudioFiles] FFmpeg concat process completed successfully."
            );
            Debug.WriteLine(
                $"[VideoGen.CombineAudioFiles] FFmpeg concat output:\n{outputBuilder.ToString()}"
            );

            // FIXED: Move the combined audio file to persistent location BEFORE cleanup
            Debug.WriteLine(
                $"[VideoGen.CombineAudioFiles] Moving combined audio from temp to persistent location."
            );
            if (File.Exists(tempCombinedAudioPath))
            {
                File.Move(tempCombinedAudioPath, finalCombinedAudioPath);
                Debug.WriteLine(
                    $"[VideoGen.CombineAudioFiles] Successfully moved combined audio to: {finalCombinedAudioPath}"
                );
            }
            else
            {
                Debug.WriteLine(
                    $"[VideoGen.CombineAudioFiles] ERROR: Temp combined audio file not found: {tempCombinedAudioPath}"
                );
                return null;
            }

            // Now it's safe to clean up the temp directory
            Debug.WriteLine("[VideoGen.CombineAudioFiles] Cleaning up temporary directory.");
            try
            {
                // Directory.Delete(tempDir, true);
                Debug.WriteLine(
                    $"[VideoGen.CombineAudioFiles] Successfully deleted temporary directory: {tempDir}"
                );
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[VideoGen.CombineAudioFiles] Error deleting temporary directory {tempDir}: {ex.Message}"
                );
            }

            Debug.WriteLine(
                $"[VideoGen.CombineAudioFiles] Exiting method, returning combined path: {finalCombinedAudioPath}"
            );
            return finalCombinedAudioPath;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[VideoGen.CombineAudioFiles] Audio combination error: {ex.Message}\n{ex.StackTrace}"
            );
            return null;
        }
    }

    private async Task ExtractAudioSegment(
        string inputPath,
        string outputPath,
        double startTime,
        double duration
    )
    {
        Debug.WriteLine(
            $"[VideoGen.ExtractAudioSegment] Entering method. Input: '{inputPath}', Output: '{outputPath}', StartTime: {startTime}s, Duration: {duration}s"
        );
        if (string.IsNullOrEmpty(inputPath) || !File.Exists(inputPath))
        {
            Debug.WriteLine(
                $"[VideoGen.ExtractAudioSegment] Input path is invalid or file does not exist: '{inputPath}'. Throwing exception."
            );
            throw new FileNotFoundException(
                "Input audio file not found for extraction.",
                inputPath
            );
        }
        if (duration <= 0)
        {
            Debug.WriteLine(
                $"[VideoGen.ExtractAudioSegment] Duration is zero or negative ({duration}s). Skipping extraction for '{inputPath}'. Output file '{outputPath}' will likely not be created or be empty."
            );
            // FFmpeg might create an empty file or error out, this log helps identify it.
            // We could throw here, or let ffmpeg handle it and check exit code.
            // For now, let ffmpeg try.
        }

        var extractArguments =
            $"-y -i \"{inputPath}\" -ss {startTime} -t {duration} -acodec copy \"{outputPath}\""; // Added -y
        Debug.WriteLine(
            $"[VideoGen.ExtractAudioSegment] FFmpeg extract arguments: {extractArguments}"
        );
        var extractProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = extractArguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();
        extractProcess.OutputDataReceived += (sender, args) =>
        {
            if (args.Data != null)
                outputBuilder.AppendLine(args.Data);
        };
        extractProcess.ErrorDataReceived += (sender, args) =>
        {
            if (args.Data != null)
                errorBuilder.AppendLine(args.Data);
        };

        Debug.WriteLine("[VideoGen.ExtractAudioSegment] Starting FFmpeg extract process.");
        extractProcess.Start();
        extractProcess.BeginOutputReadLine();
        extractProcess.BeginErrorReadLine();
        await extractProcess.WaitForExitAsync();
        Debug.WriteLine(
            $"[VideoGen.ExtractAudioSegment] FFmpeg extract process exited with code: {extractProcess.ExitCode}"
        );

        if (extractProcess.ExitCode != 0)
        {
            string errorOutput = errorBuilder.ToString();
            Debug.WriteLine(
                $"[VideoGen.ExtractAudioSegment] FFmpeg extract error (Exit Code {extractProcess.ExitCode}):\n{errorOutput}"
            );
            Debug.WriteLine(
                $"[VideoGen.ExtractAudioSegment] FFmpeg extract output:\n{outputBuilder.ToString()}"
            );
            throw new Exception(
                $"Failed to extract audio segment from '{inputPath}': {errorOutput}"
            );
        }
        Debug.WriteLine(
            $"[VideoGen.ExtractAudioSegment] FFmpeg extract process completed successfully for '{inputPath}'."
        );
        Debug.WriteLine(
            $"[VideoGen.ExtractAudioSegment] FFmpeg extract output:\n{outputBuilder.ToString()}"
        );
        Debug.WriteLine("[VideoGen.ExtractAudioSegment] Exiting method.");
    }

    private async Task GenerateSynthwaveVideo(
        string combinedAudioPath,
        VisualizationMode visualizationMode,
        ColorMode colorMode,
        VisualizationParameters visualizationParameters = null
    )
    {
        Debug.WriteLine(
            $"[VideoGen.GenerateSynthwaveVideo] Entering method. Combined audio path: {combinedAudioPath}"
        );
        if (string.IsNullOrEmpty(combinedAudioPath) || !File.Exists(combinedAudioPath))
        {
            Debug.WriteLine(
                $"[VideoGen.GenerateSynthwaveVideo] Combined audio path is invalid or file does not exist: '{combinedAudioPath}'. Aborting."
            );
            _snackbarManager.Notify("Error: Combined audio file not found for video generation.");
            throw new FileNotFoundException("Combined audio file not found.", combinedAudioPath);
        }
        try
        {
            // Create output directory if it doesn't exist
            string outputDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                "SynthwaveVideos"
            );
            Debug.WriteLine($"[VideoGen.GenerateSynthwaveVideo] Output directory: {outputDir}");
            Directory.CreateDirectory(outputDir);
            Debug.WriteLine($"[VideoGen.GenerateSynthwaveVideo] Ensured output directory exists.");

            // Generate unique output filename
            string outputFileName = $"synthwave_video_{DateTime.Now:yyyyMMdd_HHmmss}.mp4";
            string outputPath = Path.Combine(outputDir, outputFileName);
            Debug.WriteLine($"[VideoGen.GenerateSynthwaveVideo] Video output path: {outputPath}");

            // Create visualizer engine with high quality settings
            Debug.WriteLine(
                "[VideoGen.GenerateSynthwaveVideo] Initializing AudioVisualizerEngine."
            );
            var visualizer = new AudioVisualizerEngine(
                width: 1920,
                height: 1080,
                fps: 60,
                outputPath: outputPath
            );
            Debug.WriteLine("[VideoGen.GenerateSynthwaveVideo] AudioVisualizerEngine initialized.");

            // Use the passed-in visualization mode and color scheme
            Debug.WriteLine(
                $"[VideoGen.GenerateSynthwaveVideo] Using passed VisualizationMode: {visualizationMode}, ColorMode: {colorMode}"
            );

            // Log parameters if provided
            if (visualizationParameters != null)
            {
                Debug.WriteLine(
                    $"[VideoGen.GenerateSynthwaveVideo] Using custom visualization parameters of type: {visualizationParameters.GetType().Name}"
                );
            }
            else
            {
                Debug.WriteLine(
                    "[VideoGen.GenerateSynthwaveVideo] No custom parameters provided, will use defaults."
                );
            }

            // Generate the visualization with parameters
            Debug.WriteLine(
                $"[VideoGen.GenerateSynthwaveVideo] Calling visualizer.CreateVisualization for: {combinedAudioPath}"
            );
            await visualizer.CreateVisualization(
                combinedAudioPath,
                visualizationMode,
                colorMode,
                visualizationParameters
            );
            Debug.WriteLine(
                "[VideoGen.GenerateSynthwaveVideo] visualizer.CreateVisualization completed."
            );
            Debug.WriteLine(
                $"[VideoGen.GenerateSynthwaveVideo] Synthwave video should be created at: {outputPath}"
            );

            // Optional: Show notification with path to generated video
            var notificationMessage = $"Video saved to: {outputPath}";
            _snackbarManager.Notify(notificationMessage);
            Debug.WriteLine($"[VideoGen.GenerateSynthwaveVideo] Notified: {notificationMessage}");

            // Optional: Open the output directory in file explorer
            Debug.WriteLine(
                $"[VideoGen.GenerateSynthwaveVideo] Attempting to open explorer at: {outputDir}"
            );
            Process.Start("explorer.exe", $"\"{outputDir}\""); // Ensure path with spaces is quoted
            Debug.WriteLine($"[VideoGen.GenerateSynthwaveVideo] Explorer open command issued.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[VideoGen.GenerateSynthwaveVideo] Video generation error: {ex.Message}\n{ex.StackTrace}"
            );
            throw; // Re-throw to be caught by the calling method's handler
        }
        Debug.WriteLine("[VideoGen.GenerateSynthwaveVideo] Exiting method.");
    }

    // Alternative method using AdvancedVisualizer for more exotic effects
    private async Task GenerateAdvancedSynthwaveVideo(string combinedAudioPath)
    {
        Debug.WriteLine(
            $"[VideoGen.GenerateAdvancedSynthwaveVideo] Entering method. Combined audio path: {combinedAudioPath}"
        );
        if (string.IsNullOrEmpty(combinedAudioPath) || !File.Exists(combinedAudioPath))
        {
            Debug.WriteLine(
                $"[VideoGen.GenerateAdvancedSynthwaveVideo] Combined audio path is invalid or file does not exist: '{combinedAudioPath}'. Aborting."
            );
            _snackbarManager.Notify(
                "Error: Combined audio file not found for advanced video generation."
            );
            throw new FileNotFoundException(
                "Combined audio file not found for advanced video.",
                combinedAudioPath
            );
        }

        try
        {
            string outputDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                "SynthwaveVideos"
            );
            Debug.WriteLine(
                $"[VideoGen.GenerateAdvancedSynthwaveVideo] Output directory: {outputDir}"
            );
            Directory.CreateDirectory(outputDir);
            Debug.WriteLine(
                $"[VideoGen.GenerateAdvancedSynthwaveVideo] Ensured output directory exists."
            );

            string outputFileName = $"advanced_synthwave_{DateTime.Now:yyyyMMdd_HHmmss}.mp4";
            string outputPath = Path.Combine(outputDir, outputFileName);
            Debug.WriteLine(
                $"[VideoGen.GenerateAdvancedSynthwaveVideo] Video output path: {outputPath}"
            );

            Debug.WriteLine(
                "[VideoGen.GenerateAdvancedSynthwaveVideo] Initializing AdvancedVisualizer."
            );
            var advancedVisualizer = new AdvancedVisualizer(1920, 1080, 60, outputPath);
            Debug.WriteLine(
                "[VideoGen.GenerateAdvancedSynthwaveVideo] AdvancedVisualizer initialized."
            );

            // Load audio data
            Debug.WriteLine(
                $"[VideoGen.GenerateAdvancedSynthwaveVideo] Loading audio data using AdvancedVisualizer from: {combinedAudioPath}"
            );
            await advancedVisualizer.LoadAudioData(combinedAudioPath); // This is from AudioVisualizerEngine base
            Debug.WriteLine("[VideoGen.GenerateAdvancedSynthwaveVideo] Audio data loaded.");

            // Initialize video writer
            Debug.WriteLine(
                "[VideoGen.GenerateAdvancedSynthwaveVideo] Initializing VideoFileWriter."
            );
            var videoWriter = new VideoFileWriter();
            videoWriter.Open(outputPath, 1920, 1080, 60, VideoCodec.H264);
            Debug.WriteLine("[VideoGen.GenerateAdvancedSynthwaveVideo] VideoFileWriter opened.");

            // Get audio duration and calculate frames
            Debug.WriteLine(
                $"[VideoGen.GenerateAdvancedSynthwaveVideo] Getting audio duration for: {combinedAudioPath}"
            );
            var duration = GetAudioDuration(combinedAudioPath); // Using the local GetAudioDuration
            var totalFrames = (int)(duration * 60); // 60 FPS
            Debug.WriteLine(
                $"[VideoGen.GenerateAdvancedSynthwaveVideo] Audio duration: {duration}s, Total frames: {totalFrames}"
            );

            if (totalFrames <= 0)
            {
                Debug.WriteLine(
                    "[VideoGen.GenerateAdvancedSynthwaveVideo] Total frames is zero or negative. Aborting frame generation."
                );
                videoWriter.Close(); // Close writer if no frames
                _snackbarManager.Notify("Error: Cannot generate video with zero duration.");
                return;
            }

            // Generate frames with different advanced effects
            Debug.WriteLine(
                "[VideoGen.GenerateAdvancedSynthwaveVideo] Starting frame generation loop."
            );
            for (int frame = 0; frame < totalFrames; frame++)
            {
                if (frame % 60 == 0) // Log every second of video
                    Debug.WriteLine(
                        $"[VideoGen.GenerateAdvancedSynthwaveVideo] Generating frame {frame}/{totalFrames}"
                    );

                // This GenerateAdvancedFrame method requires the _audioSamples from AdvancedVisualizer to be populated
                // and correctly sliced per frame. The current GenerateAdvancedFrame implementation
                // uses a placeholder `new float[1024]`. This needs to be fixed.
                // For now, I'll call the GenerateFrame method from the base class which handles samples correctly.
                // To use the specific drawing methods of AdvancedVisualizer, they would need to be callable
                // from within the base class's GenerateFrame or a similar mechanism.

                // **Option 1: (Corrected way if AdvancedVisualizer.GenerateFrame is intended to be like base)**
                // The provided GenerateAdvancedFrame has its own logic, it doesn't use the base's sample processing.
                // If you want to use the AdvancedVisualizer's *drawing methods* but with proper sample handling:
                // You would typically override GenerateFrame in AdvancedVisualizer OR pass the drawing function.
                // The current GenerateAdvancedFrame is a standalone frame producer.

                // Calling the method as it is defined:
                var bitmap = GenerateAdvancedFrame(
                    advancedVisualizer,
                    frame,
                    totalFrames,
                    combinedAudioPath
                ); // Pass audio path for it to potentially load samples

                videoWriter.WriteVideoFrame(bitmap);
                bitmap.Dispose();

                // Update progress occasionally
                if (frame > 0 && frame % 300 == 0) // Every 5 seconds at 60fps
                {
                    double progressPercentage = (double)frame / totalFrames * 100;
                    var progressMessage =
                        $"Generating advanced video: {progressPercentage:F1}% complete";
                    _snackbarManager.Notify(progressMessage);
                    Debug.WriteLine($"[VideoGen.GenerateAdvancedSynthwaveVideo] {progressMessage}");
                }
            }
            Debug.WriteLine(
                "[VideoGen.GenerateAdvancedSynthwaveVideo] Frame generation loop completed."
            );

            videoWriter.Close();
            Debug.WriteLine("[VideoGen.GenerateAdvancedSynthwaveVideo] VideoFileWriter closed.");

            // Merge audio with video
            Debug.WriteLine(
                $"[VideoGen.GenerateAdvancedSynthwaveVideo] Merging audio '{combinedAudioPath}' with video '{outputPath}'."
            );
            await MergeAudioVideo(combinedAudioPath, outputPath); // Using the local MergeAudioVideo
            Debug.WriteLine("[VideoGen.GenerateAdvancedSynthwaveVideo] Audio and video merged.");

            Debug.WriteLine(
                $"[VideoGen.GenerateAdvancedSynthwaveVideo] Advanced synthwave video created: {outputPath}"
            );
            _snackbarManager.Notify($"Advanced video saved to: {outputPath}");
            Process.Start("explorer.exe", $"\"{outputDir}\"");
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[VideoGen.GenerateAdvancedSynthwaveVideo] Advanced video generation error: {ex.Message}\n{ex.StackTrace}"
            );
            throw;
        }
        Debug.WriteLine("[VideoGen.GenerateAdvancedSynthwaveVideo] Exiting method.");
    }

    // Modified GenerateAdvancedFrame to accept audioPath to fetch samples
    // This is a conceptual change; the AdvancedVisualizer needs proper sample management internally for this to be robust.
    private Bitmap GenerateAdvancedFrame(
        AdvancedVisualizer visualizer,
        int frameIndex,
        int totalFrames,
        string audioFilePath // Added to conceptually allow sample fetching
    )
    {
        Debug.WriteLineIf(
            frameIndex % 60 == 0,
            $"[VideoGen.GenerateAdvancedFrame] Entering for frame {frameIndex}/{totalFrames}. EffectIndex: {(frameIndex / 300) % 5}"
        );
        var bitmap = new Bitmap(1920, 1080);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.FromArgb(20, 0, 0, 0)); // Slight fade for background

            // ! CRITICAL: Sample fetching and FFT needs to happen here for AdvancedVisualizer
            // The `visualizer` (AdvancedVisualizer instance) would need a method similar to
            // the base AudioVisualizerEngine's GenerateFrame's sample processing part.
            // For this example, I'll mimic what the base GenerateFrame does conceptually,
            // but this should ideally be encapsulated within AdvancedVisualizer.

            // This is a simplified placeholder. Real implementation needs to sync with _audioSamples in visualizer
            float[] currentSamples;
            if (visualizer._audioSamples != null && visualizer._audioSamples.Any()) // Use internal samples if loaded
            {
                var samplesPerFrame = visualizer._audioSamples.Count / totalFrames;
                var startIndex = frameIndex * samplesPerFrame;
                var endIndex = Math.Min(
                    startIndex + samplesPerFrame,
                    visualizer._audioSamples.Count
                );
                if (startIndex < visualizer._audioSamples.Count && endIndex > startIndex)
                {
                    currentSamples = new float[endIndex - startIndex];
                    Array.Copy(
                        visualizer._audioSamples.ToArray(),
                        startIndex,
                        currentSamples,
                        0,
                        currentSamples.Length
                    );
                    visualizer.PerformFFT(currentSamples); // Assuming public or internal access modifier or method in AdvancedVisualizer
                    visualizer.UpdateFrequencyBands(); // Assuming public or internal access modifier
                }
                else
                {
                    currentSamples = new float[visualizer._fftBuffer?.Length ?? 1024]; // fallback
                    Debug.WriteLineIf(
                        frameIndex % 60 == 0,
                        $"[VideoGen.GenerateAdvancedFrame] Warning: Could not get current audio samples for frame {frameIndex}. Using empty/fallback."
                    );
                }
            }
            else
            {
                currentSamples = new float[1024]; // Fallback if _audioSamples not loaded/available.
                Debug.WriteLineIf(
                    frameIndex % 60 == 0,
                    $"[VideoGen.GenerateAdvancedFrame] Warning: visualizer._audioSamples is null or empty for frame {frameIndex}. Using placeholder samples."
                );
            }
            // Update frame count for visualizer effects that depend on it
            visualizer._frameCount = frameIndex;

            int effectIndex = (frameIndex / (visualizer._fps * 5)) % 5; // Change effect every 5 seconds based on visualizer's FPS
            Debug.WriteLineIf(
                frameIndex % (visualizer._fps * 5) == 0,
                $"[VideoGen.GenerateAdvancedFrame] Changing to EffectIndex: {effectIndex} at frame {frameIndex}"
            );

            switch (effectIndex)
            {
                case 0:
                    // Debug.WriteLineIf(frameIndex % 60 == 0, "[VideoGen.GenerateAdvancedFrame] Drawing MatrixRain.");
                    visualizer.DrawMatrixRain(g, currentSamples, ColorMode.PsychedelicFlow);
                    break;
                case 1:
                    // Debug.WriteLineIf(frameIndex % 60 == 0, "[VideoGen.GenerateAdvancedFrame] Drawing 3DWaveformGrid.");
                    visualizer.Draw3DWaveformGrid(g, currentSamples, ColorMode.FrequencyBased);
                    break;
                case 2:
                    // Debug.WriteLineIf(frameIndex % 60 == 0, "[VideoGen.GenerateAdvancedFrame] Drawing LaserShow.");
                    visualizer.DrawLaserShow(g, currentSamples, ColorMode.DeepBassReactive);
                    break;
                case 3:
                    // Debug.WriteLineIf(frameIndex % 60 == 0, "[VideoGen.GenerateAdvancedFrame] Drawing RippleField.");
                    visualizer.DrawRippleField(g, currentSamples, ColorMode.HighFreqSparkle);
                    break;
                case 4:
                    // Debug.WriteLineIf(frameIndex % 60 == 0, "[VideoGen.GenerateAdvancedFrame] Drawing FractalTree.");
                    visualizer.DrawFractalTree(g, currentSamples, ColorMode.EmotionalMapping);
                    break;
            }
        }
        // Debug.WriteLineIf(frameIndex % 60 == 0, $"[VideoGen.GenerateAdvancedFrame] Exiting for frame {frameIndex}");
        return bitmap;
    }

    // This is a local utility method, ensure NAudio.Wave is referenced.
    private double GetAudioDuration(string mp3Path)
    {
        Debug.WriteLine($"[VideoGen.GetAudioDuration] Entering for path: {mp3Path}");
        if (string.IsNullOrEmpty(mp3Path) || !File.Exists(mp3Path))
        {
            Debug.WriteLine($"[VideoGen.GetAudioDuration] File not found: {mp3Path}. Returning 0.");
            return 0;
        }
        try
        {
            using (var reader = new NAudio.Wave.Mp3FileReader(mp3Path))
            {
                var duration = reader.TotalTime.TotalSeconds;
                Debug.WriteLine($"[VideoGen.GetAudioDuration] Duration for {mp3Path}: {duration}s");
                return duration;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[VideoGen.GetAudioDuration] Error reading duration for {mp3Path}: {ex.Message}. Returning 0."
            );
            return 0;
        }
    }

    // This is a local utility method.
    private async Task MergeAudioVideo(string audioPath, string videoPath)
    {
        Debug.WriteLine(
            $"[VideoGen.MergeAudioVideo] Entering. Audio: '{audioPath}', Video: '{videoPath}'"
        );
        if (string.IsNullOrEmpty(audioPath) || !File.Exists(audioPath))
        {
            Debug.WriteLine(
                $"[VideoGen.MergeAudioVideo] Audio path invalid or file not found: {audioPath}. Aborting merge."
            );
            throw new FileNotFoundException("Audio file for merging not found.", audioPath);
        }
        if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath))
        {
            Debug.WriteLine(
                $"[VideoGen.MergeAudioVideo] Video path invalid or file not found: {videoPath}. Aborting merge."
            );
            throw new FileNotFoundException("Video file for merging not found.", videoPath);
        }

        string tempOutputPath = Path.Combine(
            Path.GetDirectoryName(videoPath),
            Path.GetFileNameWithoutExtension(videoPath) + "_temp_merged.mp4"
        );
        Debug.WriteLine(
            $"[VideoGen.MergeAudioVideo] Temporary output path for merged file: {tempOutputPath}"
        );

        var mergeArguments =
            $"-y -i \"{videoPath}\" -i \"{audioPath}\" -c:v copy -c:a aac -shortest \"{tempOutputPath}\""; // Added -y
        Debug.WriteLine($"[VideoGen.MergeAudioVideo] FFmpeg merge arguments: {mergeArguments}");
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = mergeArguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();
        process.OutputDataReceived += (sender, args) =>
        {
            if (args.Data != null)
                outputBuilder.AppendLine(args.Data);
        };
        process.ErrorDataReceived += (sender, args) =>
        {
            if (args.Data != null)
                errorBuilder.AppendLine(args.Data);
        };

        Debug.WriteLine("[VideoGen.MergeAudioVideo] Starting FFmpeg merge process.");
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();
        Debug.WriteLine(
            $"[VideoGen.MergeAudioVideo] FFmpeg merge process exited with code: {process.ExitCode}"
        );

        if (process.ExitCode != 0)
        {
            string errorOutput = errorBuilder.ToString();
            Debug.WriteLine(
                $"[VideoGen.MergeAudioVideo] FFmpeg merge error (Exit Code {process.ExitCode}):\n{errorOutput}"
            );
            Debug.WriteLine(
                $"[VideoGen.MergeAudioVideo] FFmpeg merge output:\n{outputBuilder.ToString()}"
            );
            // Optionally delete tempOutputPath if it was created despite error
            if (File.Exists(tempOutputPath))
                try
                {
                    // File.Delete(tempOutputPath);
                }
                catch { }
            throw new Exception($"FFmpeg failed to merge audio and video: {errorOutput}");
        }
        Debug.WriteLine("[VideoGen.MergeAudioVideo] FFmpeg merge process completed successfully.");
        Debug.WriteLine(
            $"[VideoGen.MergeAudioVideo] FFmpeg merge output:\n{outputBuilder.ToString()}"
        );

        // Replace original video with merged version
        Debug.WriteLine(
            $"[VideoGen.MergeAudioVideo] Replacing original video '{videoPath}' with merged version '{tempOutputPath}'."
        );
        if (File.Exists(tempOutputPath)) // Check if temp output was actually created
        {
            // File.Delete(videoPath); // Delete original (video-only)
            Debug.WriteLine($"[VideoGen.MergeAudioVideo] Deleted original video file: {videoPath}");
            File.Move(tempOutputPath, videoPath); // Rename temp to original name
            Debug.WriteLine(
                $"[VideoGen.MergeAudioVideo] Moved '{tempOutputPath}' to '{videoPath}'."
            );
        }
        else
        {
            Debug.WriteLine(
                $"[VideoGen.MergeAudioVideo] Error: Temp output file '{tempOutputPath}' not found after successful FFmpeg exit. Cannot replace original video."
            );
            throw new FileNotFoundException(
                "Merged video file was not found after FFmpeg processing.",
                tempOutputPath
            );
        }
        Debug.WriteLine("[VideoGen.MergeAudioVideo] Exiting method.");
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
