using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using YoutubeDownloader.Core.Downloading;
using YoutubeDownloader.Core.Utils.Extensions;
using YoutubeDownloader.ViewModels;
using YoutubeDownloader.ViewModels.Components;
using YoutubeDownloader.ViewModels.Dialogs;
using YoutubeExplode.Videos;

namespace YoutubeDownloader.Framework;

public class ViewModelManager(IServiceProvider services)
{
    public MainViewModel CreateMainViewModel() => services.GetRequiredService<MainViewModel>();

    public DashboardViewModel CreateDashboardViewModel() =>
        services.GetRequiredService<DashboardViewModel>();

    public AuthSetupViewModel CreateAuthSetupViewModel() =>
        services.GetRequiredService<AuthSetupViewModel>();

    public DownloadViewModel CreateDownloadViewModel(
        IVideo video,
        VideoDownloadOption downloadOption,
        string filePath
    )
    {
        var viewModel = services.GetRequiredService<DownloadViewModel>();
        viewModel.Video = video;
        viewModel.DownloadOption = downloadOption;
        viewModel.FilePath = filePath;
        return viewModel;
    }

    public DownloadViewModel CreateDownloadViewModel(
        IVideo video,
        VideoDownloadPreference downloadPreference,
        string filePath
    )
    {
        var viewModel = services.GetRequiredService<DownloadViewModel>();
        viewModel.Video = video;
        viewModel.DownloadPreference = downloadPreference;
        viewModel.FilePath = filePath;
        return viewModel;
    }

    public MediaPlayerViewModel CreateMediaPlayerViewModel(
        string filePath,
        DownloadViewModel downloadViewModel
    ) => new(filePath, downloadViewModel);

    public DownloadMultipleSetupViewModel CreateDownloadMultipleSetupViewModel(
        string title,
        IReadOnlyList<IVideo> availableVideos,
        bool preselectVideos = true
    )
    {
        var viewModel = services.GetRequiredService<DownloadMultipleSetupViewModel>();
        viewModel.Title = title;
        viewModel.AvailableVideos = availableVideos;
        if (preselectVideos)
            viewModel.SelectedVideos.AddRange(availableVideos);
        return viewModel;
    }

    public DownloadSingleSetupViewModel CreateDownloadSingleSetupViewModel(
        IVideo video,
        IReadOnlyList<VideoDownloadOption> availableDownloadOptions
    )
    {
        var viewModel = services.GetRequiredService<DownloadSingleSetupViewModel>();
        viewModel.Video = video;
        viewModel.AvailableDownloadOptions = availableDownloadOptions;
        return viewModel;
    }

    public MessageBoxViewModel CreateMessageBoxViewModel(
        string title,
        string message,
        string? okButtonText,
        string? cancelButtonText
    )
    {
        var viewModel = services.GetRequiredService<MessageBoxViewModel>();
        viewModel.Title = title;
        viewModel.Message = message;
        viewModel.DefaultButtonText = okButtonText;
        viewModel.CancelButtonText = cancelButtonText;
        return viewModel;
    }

    public MessageBoxViewModel CreateMessageBoxViewModel(string title, string message) =>
        CreateMessageBoxViewModel(title, message, "CLOSE", null);

    public SettingsViewModel CreateSettingsViewModel() =>
        services.GetRequiredService<SettingsViewModel>();

    /// <summary>
    /// Creates a confirmation dialog view model with Yes/No buttons
    /// </summary>
    /// <param name="title">Dialog title</param>
    /// <param name="message">Dialog message</param>
    /// <param name="yesButtonText">Text for the confirm/yes button</param>
    /// <param name="noButtonText">Text for the cancel/no button</param>
    /// <returns>A configured confirmation dialog view model</returns>
    public ConfirmationDialogViewModel CreateConfirmationViewModel(
        string title,
        string message,
        string yesButtonText = "YES",
        string noButtonText = "NO"
    )
    {
        var viewModel = services.GetRequiredService<ConfirmationDialogViewModel>();
        viewModel.Title = title;
        viewModel.Message = message;
        viewModel.ConfirmButtonText = yesButtonText;
        viewModel.CancelButtonText = noButtonText;
        return viewModel;
    }

    /// <summary>
    /// Creates a working directory selection dialog view model
    /// </summary>
    /// <param name="title">Dialog title</param>
    /// <param name="message">Dialog message</param>
    /// <param name="initialDirectory">Initial directory path</param>
    /// <returns>A configured working directory selection dialog view model</returns>
    public WorkingDirectoryDialogViewModel CreateWorkingDirectoryDialogViewModel(
        string title = "Select Working Directory",
        string message = "Choose a directory where MP3 files will be saved:",
        string? initialDirectory = null
    )
    {
        var viewModel = services.GetRequiredService<WorkingDirectoryDialogViewModel>();
        viewModel.Title = title;
        viewModel.Message = message;
        viewModel.InitialDirectory = initialDirectory;
        return viewModel;
    }
}
