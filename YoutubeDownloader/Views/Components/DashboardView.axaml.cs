using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using YoutubeDownloader.Framework;
using YoutubeDownloader.ViewModels.Components;

namespace YoutubeDownloader.Views.Components;

public partial class DashboardView : UserControl<DashboardViewModel>
{
    public DashboardView()
    {
        InitializeComponent();
        // Bind the event with the tunnel strategy to handle keys that take part in writing text
        QueryTextBox.AddHandler(KeyDownEvent, QueryTextBox_OnKeyDown, RoutingStrategies.Tunnel);
    }

    private async void UserControl_OnLoaded(object? sender, RoutedEventArgs args)
    {
        // Focus the query textbox
        QueryTextBox.Focus();

        // If ViewModel is available, prompt for working directory
        if (DataContext is DashboardViewModel viewModel)
        {
            await viewModel.PromptForWorkingDirectoryAsync();
        }
    }

    private void QueryTextBox_OnKeyDown(object? sender, KeyEventArgs args)
    {
        // When pressing Enter without Shift, execute the default button command
        // instead of adding a new line.
        if (args.Key == Key.Enter && args.KeyModifiers != KeyModifiers.Shift)
        {
            args.Handled = true;
            ProcessQueryButton.Command?.Execute(ProcessQueryButton.CommandParameter);
        }
    }

    private void StatusTextBlock_OnPointerReleased(object sender, PointerReleasedEventArgs args)
    {
        if (sender is IDataContextProvider { DataContext: DownloadViewModel dataContext })
            dataContext.CopyErrorMessageCommand.Execute(null);
    }
}
