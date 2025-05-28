using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using YoutubeDownloader.Framework;

namespace YoutubeDownloader.Views.Dialogs;

public partial class ConfirmDialogView : UserControl
{
    public ConfirmDialogView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
