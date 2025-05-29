using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Platform.Storage;
using DialogHostAvalonia;
using YoutubeDownloader.Utils.Extensions;

namespace YoutubeDownloader.Framework;

public class DialogManager : IDisposable
{
    private readonly SemaphoreSlim _dialogLock = new(1, 1);

    public async Task<T?> ShowDialogAsync<T>(DialogViewModelBase<T> dialog)
    {
        await _dialogLock.WaitAsync();
        try
        {
            await DialogHost.Show(
                dialog,
                // It's fine to await in a void method here because it's an event handler
                // ReSharper disable once AsyncVoidLambda
                async (object _, DialogOpenedEventArgs args) =>
                {
                    await dialog.WaitForCloseAsync();
                    try
                    {
                        args.Session.Close();
                    }
                    catch (InvalidOperationException)
                    {
                        // Dialog host is already processing a close operation
                    }
                }
            );
            return dialog.DialogResult;
        }
        finally
        {
            _dialogLock.Release();
        }
    }

    /// <summary>
    /// Shows a file picker dialog to select an existing file
    /// </summary>
    /// <param name="title">The title for the file picker dialog</param>
    /// <param name="defaultFilePath">The default file path to start in</param>
    /// <param name="fileTypes">Array of file type filters in format "Description|*.ext1;*.ext2"</param>
    /// <returns>The selected file path or null if canceled</returns>
    public async Task<string?> ShowFilePickerAsync(
        string title,
        string defaultFilePath = "",
        string[]? fileTypes = null
    )
    {
        var topLevel =
            Application.Current?.ApplicationLifetime?.TryGetTopLevel()
            ?? throw new ApplicationException("Could not find the top-level visual element.");

        // Convert string array to FilePickerFileType list
        List<FilePickerFileType>? filePickerTypes = null;
        if (fileTypes != null)
        {
            filePickerTypes = new List<FilePickerFileType>();
            foreach (var fileType in fileTypes)
            {
                var parts = fileType.Split('|');
                if (parts.Length == 2)
                {
                    var patterns = parts[1].Split(';');
                    filePickerTypes.Add(new FilePickerFileType(parts[0]) { Patterns = patterns });
                }
            }
        }

        // Try to get the folder from the default path if provided
        IStorageFolder? startLocation = null;
        if (!string.IsNullOrEmpty(defaultFilePath) && File.Exists(defaultFilePath))
        {
            var directory = Path.GetDirectoryName(defaultFilePath);
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                startLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(directory);
            }
        }

        // Create file picker options
        var options = new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = title,
            SuggestedStartLocation = startLocation,
            FileTypeFilter = filePickerTypes,
        };

        // Open the file picker
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(options);

        // Return the first file's path or null if none was selected
        return files.FirstOrDefault()?.Path.LocalPath;
    }

    public async Task<string?> PromptSaveFilePathAsync(
        IReadOnlyList<FilePickerFileType>? fileTypes = null,
        string defaultFilePath = ""
    )
    {
        var topLevel =
            Application.Current?.ApplicationLifetime?.TryGetTopLevel()
            ?? throw new ApplicationException("Could not find the top-level visual element.");
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                FileTypeChoices = fileTypes,
                SuggestedFileName = defaultFilePath,
                DefaultExtension = Path.GetExtension(defaultFilePath).TrimStart('.'),
            }
        );
        return file?.Path.LocalPath;
    }

    public async Task<string?> PromptDirectoryPathAsync(string defaultDirPath = "")
    {
        var topLevel =
            Application.Current?.ApplicationLifetime?.TryGetTopLevel()
            ?? throw new ApplicationException("Could not find the top-level visual element.");
        var startLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(
            defaultDirPath
        );
        var folderPickResult = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                AllowMultiple = false,
                SuggestedStartLocation = startLocation,
            }
        );
        return folderPickResult.FirstOrDefault()?.Path.LocalPath;
    }

    /// <summary>
    /// Shows a folder picker dialog to select a working directory, with custom title and description.
    /// </summary>
    /// <param name="title">The title for the folder picker dialog</param>
    /// <param name="defaultDirPath">The default directory path to start in</param>
    /// <returns>The selected folder path or null if canceled</returns>
    public async Task<string?> ShowFolderPickerAsync(string title, string defaultDirPath = "")
    {
        var topLevel =
            Application.Current?.ApplicationLifetime?.TryGetTopLevel()
            ?? throw new ApplicationException("Could not find the top-level visual element.");

        // Try to get the folder from the default path if provided
        IStorageFolder? startLocation = null;
        if (!string.IsNullOrEmpty(defaultDirPath) && Directory.Exists(defaultDirPath))
        {
            startLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(
                defaultDirPath
            );
        }

        // Create folder picker options
        var options = new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = title,
            SuggestedStartLocation = startLocation,
        };

        // Open the folder picker
        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(options);

        // Return the first folder's path or null if none was selected
        return folders.FirstOrDefault()?.Path.LocalPath;
    }

    /// <summary>
    /// Creates a specified directory if it doesn't already exist
    /// </summary>
    /// <param name="directoryPath">Path to create</param>
    /// <returns>True if the directory was created or already exists</returns>
    public bool EnsureDirectoryExists(string directoryPath)
    {
        try
        {
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Shows a confirmation dialog with Yes/No options
    /// </summary>
    /// <param name="title">Dialog title</param>
    /// <param name="message">Dialog message</param>
    /// <param name="viewModelManager">View model manager instance</param>
    /// <returns>True if confirmed, false otherwise</returns>
    public async Task<bool> ShowConfirmationDialogAsync(
        string title,
        string message,
        ViewModelManager viewModelManager
    )
    {
        var dialog = viewModelManager.CreateConfirmationViewModel(title, message);
        var result = await ShowDialogAsync(dialog);
        return result == true;
    }

    public void Dispose() => _dialogLock.Dispose();
}
