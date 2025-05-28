using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YoutubeDownloader.Framework;

namespace YoutubeDownloader.ViewModels.Dialogs;

public partial class WorkingDirectoryDialogViewModel : DialogViewModelBase<string?>
{
    private readonly DialogManager _dialogManager;

    public WorkingDirectoryDialogViewModel(DialogManager dialogManager)
    {
        _dialogManager = dialogManager;
    }

    [ObservableProperty]
    private string _title = "Select Working Directory";

    [ObservableProperty]
    private string _message = "Choose a directory where MP3 files will be saved:";

    [ObservableProperty]
    private string? _initialDirectory;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string? _selectedDirectory;

    private bool CanConfirm() =>
        !string.IsNullOrEmpty(SelectedDirectory) && Directory.Exists(SelectedDirectory);

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var directory = await _dialogManager.ShowFolderPickerAsync(
            "Select Working Directory",
            InitialDirectory ?? ""
        );

        if (!string.IsNullOrEmpty(directory))
        {
            SelectedDirectory = directory;
        }
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        Close(SelectedDirectory); // Use the Close method from the base class
    }

    [RelayCommand]
    private void Cancel()
    {
        Close(null); // Use the Close method from the base class
    }

    [RelayCommand]
    private void UseDefaultLocation()
    {
        // Set a default location such as My Music/YouTubeDownloader
        var defaultLocation = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            "YouTubeDownloader"
        );

        // Create the directory if it doesn't exist
        if (!Directory.Exists(defaultLocation))
        {
            Directory.CreateDirectory(defaultLocation);
        }

        SelectedDirectory = defaultLocation;
    }
}
