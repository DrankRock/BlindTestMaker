using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YoutubeDownloader.Framework;

namespace YoutubeDownloader.ViewModels.Dialogs;

public partial class ConfirmationDialogViewModel : DialogViewModelBase<bool>
{
    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _message = "";

    [ObservableProperty]
    private string _confirmButtonText = "YES";

    [ObservableProperty]
    private string _cancelButtonText = "NO";

    [RelayCommand]
    private void Confirm()
    {
        Close(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        Close(false);
    }
}
