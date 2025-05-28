using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace YoutubeDownloader.Views.Dialogs;

public partial class WorkingDirectoryDialogView : UserControl
{
    public WorkingDirectoryDialogView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
