using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using YoutubeDownloader.Framework;
using YoutubeDownloader.Services;
using YoutubeDownloader.Utils;
using YoutubeDownloader.Utils.Extensions;

namespace YoutubeDownloader.ViewModels.Dialogs;

public partial class SettingsViewModel : DialogViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly ViewModelManager _viewModelManager;
    private readonly DialogManager _dialogManager;
    private readonly DisposableCollector _eventRoot = new();

    public SettingsViewModel(
        SettingsService settingsService,
        ViewModelManager viewModelManager,
        DialogManager dialogManager
    )
    {
        _settingsService = settingsService;
        _viewModelManager = viewModelManager;
        _dialogManager = dialogManager;
        _eventRoot.Add(_settingsService.WatchAllProperties(OnAllPropertiesChanged));
    }

    public IReadOnlyList<ThemeVariant> AvailableThemes { get; } = Enum.GetValues<ThemeVariant>();

    public ThemeVariant Theme
    {
        get => _settingsService.Theme;
        set => _settingsService.Theme = value;
    }

    public bool IsAutoUpdateEnabled
    {
        get => _settingsService.IsAutoUpdateEnabled;
        set => _settingsService.IsAutoUpdateEnabled = value;
    }

    public bool IsAuthPersisted
    {
        get => _settingsService.IsAuthPersisted;
        set => _settingsService.IsAuthPersisted = value;
    }

    public bool ShouldInjectLanguageSpecificAudioStreams
    {
        get => _settingsService.ShouldInjectLanguageSpecificAudioStreams;
        set => _settingsService.ShouldInjectLanguageSpecificAudioStreams = value;
    }

    public bool ShouldInjectSubtitles
    {
        get => _settingsService.ShouldInjectSubtitles;
        set => _settingsService.ShouldInjectSubtitles = value;
    }

    public bool ShouldInjectTags
    {
        get => _settingsService.ShouldInjectTags;
        set => _settingsService.ShouldInjectTags = value;
    }

    public bool ShouldSkipExistingFiles
    {
        get => _settingsService.ShouldSkipExistingFiles;
        set => _settingsService.ShouldSkipExistingFiles = value;
    }

    public string FileNameTemplate
    {
        get => _settingsService.FileNameTemplate;
        set => _settingsService.FileNameTemplate = value;
    }

    public int ParallelLimit
    {
        get => _settingsService.ParallelLimit;
        set => _settingsService.ParallelLimit = Math.Clamp(value, 1, 10);
    }

    // Working Directory property
    public string WorkingDirectory
    {
        get => _settingsService.LastWorkingDirectory ?? string.Empty;
        set
        {
            if (_settingsService.LastWorkingDirectory != value)
            {
                _settingsService.LastWorkingDirectory = value;
                OnPropertyChanged();
            }
        }
    }

    // Command to browse for working directory - USING THE ACTUAL FOLDER PICKER
    [RelayCommand]
    private async Task BrowseWorkingDirectoryAsync()
    {
        try
        {
            var selectedDirectory = await _dialogManager.ShowFolderPickerAsync(
                "Select Working Directory",
                WorkingDirectory ?? ""
            );

            if (!string.IsNullOrEmpty(selectedDirectory))
            {
                WorkingDirectory = selectedDirectory;
                _settingsService.Save();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error browsing for directory: {ex.Message}");
        }
    }

    // Command to open working directory in file explorer
    [RelayCommand]
    private void OpenWorkingDirectory()
    {
        try
        {
            if (!string.IsNullOrEmpty(WorkingDirectory) && Directory.Exists(WorkingDirectory))
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = WorkingDirectory,
                        UseShellExecute = true,
                        Verb = "open",
                    }
                );
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error opening directory: {ex.Message}");
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _eventRoot.Dispose();
        }
        base.Dispose(disposing);
    }
}
