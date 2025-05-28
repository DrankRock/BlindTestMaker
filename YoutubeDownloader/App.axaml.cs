using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using AsyncImageLoader;
using AsyncImageLoader.Loaders;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AvaloniaWebView;
using Material.Styles.Themes;
using Microsoft.Extensions.DependencyInjection;
using YoutubeDownloader.Framework;
using YoutubeDownloader.Services;
using YoutubeDownloader.Utils;
using YoutubeDownloader.Utils.Extensions;
using YoutubeDownloader.ViewModels;
using YoutubeDownloader.ViewModels.Components;
using YoutubeDownloader.ViewModels.Dialogs;
using YoutubeDownloader.Views;

namespace YoutubeDownloader;

public class App : Application, IDisposable
{
    private readonly DisposableCollector _eventRoot = new();

    private readonly ServiceProvider _services;
    private readonly SettingsService _settingsService;
    private readonly MainViewModel _mainViewModel;

    public App()
    {
        var services = new ServiceCollection();

        // Framework
        services.AddSingleton<DialogManager>();
        services.AddSingleton<SnackbarManager>();
        services.AddSingleton<ViewManager>();
        services.AddSingleton<ViewModelManager>();

        // Services
        services.AddSingleton<SettingsService>();
        services.AddSingleton<UpdateService>();

        // View models
        services.AddTransient<MainViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<DownloadViewModel>();
        services.AddTransient<AuthSetupViewModel>();
        services.AddTransient<DownloadMultipleSetupViewModel>();
        services.AddTransient<DownloadSingleSetupViewModel>();
        services.AddTransient<MessageBoxViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<ConfirmationDialogViewModel>();
        services.AddTransient<WorkingDirectoryDialogViewModel>();

        _services = services.BuildServiceProvider(true);
        _settingsService = _services.GetRequiredService<SettingsService>();
        _mainViewModel = _services.GetRequiredService<ViewModelManager>().CreateMainViewModel();

        // Re-initialize the theme when the user changes it
        _eventRoot.Add(
            _settingsService.WatchProperty(
                o => o.Theme,
                () =>
                {
                    RequestedThemeVariant = _settingsService.Theme switch
                    {
                        ThemeVariant.Light => Avalonia.Styling.ThemeVariant.Light,
                        ThemeVariant.Dark => Avalonia.Styling.ThemeVariant.Dark,
                        _ => Avalonia.Styling.ThemeVariant.Default,
                    };

                    InitializeTheme();
                }
            )
        );
    }

    public override void Initialize()
    {
        base.Initialize();

        AvaloniaXamlLoader.Load(this);
    }

    public override void RegisterServices()
    {
        base.RegisterServices();

        AvaloniaWebViewBuilder.Initialize(config => config.IsInPrivateModeEnabled = true);
    }

    private void InitializeTheme()
    {
        var actualTheme = RequestedThemeVariant?.Key switch
        {
            "Light" => PlatformThemeVariant.Light,
            "Dark" => PlatformThemeVariant.Dark,
            _ => PlatformSettings?.GetColorValues().ThemeVariant ?? PlatformThemeVariant.Light,
        };

        this.LocateMaterialTheme<MaterialThemeBase>().CurrentTheme =
            actualTheme == PlatformThemeVariant.Light
                ? Theme.Create(Theme.Light, Color.Parse("#343838"), Color.Parse("#F9A825"))
                : Theme.Create(Theme.Dark, Color.Parse("#E8E8E8"), Color.Parse("#F9A825"));
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainView { DataContext = _mainViewModel };

        base.OnFrameworkInitializationCompleted();

        // Configure AsyncImageLoader to handle resource URLs
        ConfigureAsyncImageLoader();

        // Set up custom theme colors
        InitializeTheme();

        // Load settings
        _settingsService.Load();
    }

    private void ConfigureAsyncImageLoader()
    {
        // Set a custom loader that handles resource URLs
        ImageLoader.AsyncImageLoader = new CustomResourceImageLoader();
    }

    private void Application_OnActualThemeVariantChanged(object? sender, EventArgs args) =>
        // Re-initialize the theme when the system theme changes
        InitializeTheme();

    public void Dispose()
    {
        _eventRoot.Dispose();
        _services.Dispose();
    }
}

// Custom image loader that handles web URLs, Avalonia resource URLs, and local file paths
public class CustomResourceImageLoader : IAsyncImageLoader
{
    private readonly HttpClient _httpClient;

    public CustomResourceImageLoader()
    {
        _httpClient = new HttpClient();
    }

    public async Task<Bitmap?> ProvideImageAsync(string url)
    {
        try
        {
            byte[]? imageData = null;

            // Handle Avalonia resource URLs
            if (url.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(url);
                if (AssetLoader.Exists(uri)) // Check if asset exists
                {
                    using var stream = AssetLoader.Open(uri);
                    if (stream != null)
                    {
                        using var memoryStream = new MemoryStream();
                        await stream.CopyToAsync(memoryStream);
                        imageData = memoryStream.ToArray();
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Asset not found: {url}");
                }
            }
            // Handle regular web URLs
            else if (
                url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            )
            {
                var response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    imageData = await response.Content.ReadAsByteArrayAsync();
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Failed to download image from {url}. Status: {response.StatusCode}"
                    );
                }
            }
            // Handle local file paths
            else if (File.Exists(url))
            {
                imageData = await File.ReadAllBytesAsync(url);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine(
                    $"URL type not supported or file not found: {url}"
                );
            }

            if (imageData != null && imageData.Length > 0)
            {
                using var memoryStream = new MemoryStream(imageData);
                return new Bitmap(memoryStream);
            }

            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load image from {url}: {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        GC.SuppressFinalize(this); // Recommended for IDisposable pattern
    }
}
