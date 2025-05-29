using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using YoutubeDownloader.Core.AudioVisualisation;
using YoutubeDownloader.Framework;
using YoutubeDownloader.Services;
using YoutubeDownloader.Utils;
using YoutubeDownloader.Utils.Extensions;

namespace YoutubeDownloader.ViewModels.Dialogs;

public partial class AudioVisualizationSettingsViewModel : DialogViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly ViewModelManager _viewModelManager;
    private readonly DialogManager _dialogManager;
    private readonly DisposableCollector _eventRoot = new();

    public AudioVisualizationSettingsViewModel(
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

    public IReadOnlyList<VisualizationMode> AvailableVisualizationModes { get; } =
        Enum.GetValues<VisualizationMode>();
    public IReadOnlyList<ColorMode> AvailableColorModes { get; } = Enum.GetValues<ColorMode>();

    public VisualizationMode VisualizationMode
    {
        get => _settingsService.VisualizationMode;
        set
        {
            _settingsService.VisualizationMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsBasicWaveformMode));
            OnPropertyChanged(nameof(IsCircularWaveMode));
            OnPropertyChanged(nameof(IsSphericalPulseMode));
            OnPropertyChanged(nameof(IsSpectrumBarsMode));
            OnPropertyChanged(nameof(IsParticleFlowMode));
            OnPropertyChanged(nameof(IsKaleidoscopeWaveMode));
            OnPropertyChanged(nameof(IsDNAHelixMode));
            OnPropertyChanged(nameof(IsAuroraMode));
            OnPropertyChanged(nameof(IsCircularSpectrumBarsMode));
        }
    }

    public ColorMode ColorMode
    {
        get => _settingsService.ColorMode;
        set => _settingsService.ColorMode = value;
    }

    public int IntervalBetweenVideos
    {
        get => _settingsService.IntervalBetweenVideos;
        set => _settingsService.IntervalBetweenVideos = Math.Clamp(value, 0, 300);
    }

    public string BackgroundVideoPath
    {
        get => _settingsService.BackgroundVideoPath ?? string.Empty;
        set
        {
            if (_settingsService.BackgroundVideoPath != value)
            {
                _settingsService.BackgroundVideoPath = value;
                OnPropertyChanged();
            }
        }
    }

    public string BackgroundImagePath
    {
        get => _settingsService.BackgroundImagePath ?? string.Empty;
        set
        {
            if (_settingsService.BackgroundImagePath != value)
            {
                _settingsService.BackgroundImagePath = value;
                OnPropertyChanged();
            }
        }
    }

    public string CircleCenterImageFilePath
    {
        get => _settingsService.CircleCenterFilePath ?? string.Empty;
        set
        {
            var newValue = string.IsNullOrWhiteSpace(value) ? null : value;
            if (_settingsService.CircleCenterFilePath != newValue)
            {
                _settingsService.CircleCenterFilePath = newValue;
                OnPropertyChanged();
                _settingsService.Save();
            }
        }
    }

    // Common visualization settings
    public double ZoomLevel
    {
        get => _settingsService.ZoomLevel;
        set => _settingsService.ZoomLevel = Math.Clamp(value, 0.1, 5.0);
    }

    public double XPosition
    {
        get => _settingsService.XPosition;
        set => _settingsService.XPosition = Math.Clamp(value, -1.0, 1.0);
    }

    public double YPosition
    {
        get => _settingsService.YPosition;
        set => _settingsService.YPosition = Math.Clamp(value, -1.0, 1.0);
    }

    // Spherical-specific settings
    public double SphereDiameter
    {
        get => _settingsService.SphereDiameter;
        set => _settingsService.SphereDiameter = Math.Clamp(value, 0.1, 2.0);
    }

    // Waveform-specific settings
    public double WaveAmplitude
    {
        get => _settingsService.WaveAmplitude;
        set => _settingsService.WaveAmplitude = Math.Clamp(value, 0.1, 3.0);
    }

    public double WaveFrequency
    {
        get => _settingsService.WaveFrequency;
        set => _settingsService.WaveFrequency = Math.Clamp(value, 0.1, 5.0);
    }

    public float LineThickness
    {
        get => _settingsService.LineThickness;
        set => _settingsService.LineThickness = Math.Clamp(value, 1f, 50f);
    }

    // Spectrum bars settings
    public int BarCount
    {
        get => _settingsService.BarCount;
        set => _settingsService.BarCount = Math.Clamp(value, 10, 200);
    }

    public double BarSpacing
    {
        get => _settingsService.BarSpacing;
        set => _settingsService.BarSpacing = Math.Clamp(value, 0.1, 2.0);
    }

    // Particle settings
    public int ParticleCount
    {
        get => _settingsService.ParticleCount;
        set => _settingsService.ParticleCount = Math.Clamp(value, 50, 2000);
    }

    public double ParticleSpeed
    {
        get => _settingsService.ParticleSpeed;
        set => _settingsService.ParticleSpeed = Math.Clamp(value, 0.1, 3.0);
    }

    // Kaleidoscope settings
    public int KaleidoscopeSegments
    {
        get => _settingsService.KaleidoscopeSegments;
        set => _settingsService.KaleidoscopeSegments = Math.Clamp(value, 3, 12);
    }

    public double RotationSpeed
    {
        get => _settingsService.RotationSpeed;
        set => _settingsService.RotationSpeed = Math.Clamp(value, 0.1, 5.0);
    }

    // DNA Helix settings
    public double HelixRadius
    {
        get => _settingsService.HelixRadius;
        set => _settingsService.HelixRadius = Math.Clamp(value, 0.1, 1.0);
    }

    public double HelixPitch
    {
        get => _settingsService.HelixPitch;
        set => _settingsService.HelixPitch = Math.Clamp(value, 0.1, 2.0);
    }

    // Aurora settings
    public double AuroraIntensity
    {
        get => _settingsService.AuroraIntensity;
        set => _settingsService.AuroraIntensity = Math.Clamp(value, 0.1, 2.0);
    }

    public double AuroraFlowSpeed
    {
        get => _settingsService.AuroraFlowSpeed;
        set => _settingsService.AuroraFlowSpeed = Math.Clamp(value, 0.1, 3.0);
    }

    public bool IsCircularSpectrumBarsMode =>
        VisualizationMode == VisualizationMode.CircularSpectrumBars;

    public int CircularBarCount
    {
        get => _settingsService.BarCount;
        set => _settingsService.BarCount = Math.Clamp(value, 10, 200);
    }

    public double CircularAmplitudeMultiplier
    {
        get => _settingsService.WaveAmplitude;
        set => _settingsService.WaveAmplitude = Math.Clamp(value, 1.0, 50.0);
    }

    public bool CircularLogarithmicScale
    {
        get => _settingsService.SpectrumLogarithmicScale;
        set => _settingsService.SpectrumLogarithmicScale = value;
    }

    public double CircularCenterX
    {
        get => _settingsService.XPosition;
        set => _settingsService.XPosition = Math.Clamp(value, 0.0, 1.0);
    }

    public double CircularCenterY
    {
        get => _settingsService.YPosition;
        set => _settingsService.YPosition = Math.Clamp(value, 0.0, 1.0);
    }

    public string CircularCircleCenterFilePath
    {
        get => _settingsService.CircleCenterFilePath ?? string.Empty;
        set
        {
            var newValue = string.IsNullOrWhiteSpace(value) ? null : value;
            if (_settingsService.CircleCenterFilePath != newValue)
            {
                _settingsService.CircleCenterFilePath = newValue;
                OnPropertyChanged();
                _settingsService.Save();
            }
        }
    }

    public double CircularBaseRadiusPercentage
    {
        get => _settingsService.BaseRadius;
        set => _settingsService.BaseRadius = (float)Math.Clamp(value, 0.05, 0.5);
    }

    public double CircularBarFillRatio
    {
        get => _settingsService.CircularSpectrumBarFillRatio;
        set => _settingsService.CircularSpectrumBarFillRatio = (float)Math.Clamp(value, 0.1, 1.0);
    }

    public double CircularBarHeightScaleFactor
    {
        get => _settingsService.CircularSpectrumBarHeightScaleFactor;
        set =>
            _settingsService.CircularSpectrumBarHeightScaleFactor = (float)
                Math.Clamp(value, 0.1, 5.0);
    }

    public double CircularMaxBarHeightRatio
    {
        get => _settingsService.CircularSpectrumMaxBarHeightRatio;
        set =>
            _settingsService.CircularSpectrumMaxBarHeightRatio = (float)Math.Clamp(value, 0.1, 2.0);
    }

    public bool CircularMirrorBars
    {
        get => _settingsService.CircularSpectrumMirrorBars;
        set => _settingsService.CircularSpectrumMirrorBars = value;
    }

    public bool CircularEnableGlow
    {
        get => _settingsService.CircularSpectrumEnableGlow;
        set => _settingsService.CircularSpectrumEnableGlow = value;
    }

    public int CircularGlowIntensity
    {
        get => (int)_settingsService.CircularSpectrumGlowIntensity;
        set => _settingsService.CircularSpectrumGlowIntensity = Math.Clamp(value, 0, 255);
    }

    public double CircularGlowOffset
    {
        get => _settingsService.CircularSpectrumGlowOffset;
        set => _settingsService.CircularSpectrumGlowOffset = (float)Math.Clamp(value, 0.0, 10.0);
    }

    public double CircularGlowAngularSpread
    {
        get => _settingsService.CircularSpectrumGlowAngularSpread;
        set =>
            _settingsService.CircularSpectrumGlowAngularSpread = (float)Math.Clamp(value, 0.0, 0.1);
    }

    // Visibility properties for mode-specific settings
    public bool IsBasicWaveformMode => VisualizationMode == VisualizationMode.BasicWaveform;
    public bool IsCircularWaveMode => VisualizationMode == VisualizationMode.CircularWave;
    public bool IsSphericalPulseMode => VisualizationMode == VisualizationMode.SphericalPulse;
    public bool IsSpectrumBarsMode => VisualizationMode == VisualizationMode.SpectrumBars;
    public bool IsParticleFlowMode => VisualizationMode == VisualizationMode.ParticleFlow;
    public bool IsKaleidoscopeWaveMode => VisualizationMode == VisualizationMode.KaleidoscopeWave;
    public bool IsDNAHelixMode => VisualizationMode == VisualizationMode.DNA_Helix;
    public bool IsAuroraMode => VisualizationMode == VisualizationMode.Aurora;

    // Command to browse for background video
    [RelayCommand]
    private async Task BrowseBackgroundVideoAsync()
    {
        try
        {
            var selectedFile = await _dialogManager.ShowFilePickerAsync(
                "Select Background Video",
                BackgroundVideoPath ?? "",
                new[] { "Video Files|*.mp4;*.avi;*.mov;*.mkv", "All Files|*.*" }
            );

            if (!string.IsNullOrEmpty(selectedFile))
            {
                BackgroundVideoPath = selectedFile;
                _settingsService.Save();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error browsing for video file: {ex.Message}");
        }
    }

    // Command to browse for background image
    [RelayCommand]
    private async Task BrowseBackgroundImageAsync()
    {
        try
        {
            var selectedFile = await _dialogManager.ShowFilePickerAsync(
                "Select Background Image",
                BackgroundImagePath ?? "",
                new[] { "Image Files|*.jpg;*.jpeg;*.png;*.bmp", "All Files|*.*" }
            );

            if (!string.IsNullOrEmpty(selectedFile))
            {
                BackgroundImagePath = selectedFile;
                _settingsService.Save();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error browsing for image file: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task BrowseCircleCenterImageAsync()
    {
        try
        {
            var selectedFile = await _dialogManager.ShowFilePickerAsync(
                "Select Circle Center PNG Image",
                CircleCenterImageFilePath ?? "", // Use the ViewModel property
                new[] { "PNG Files|*.png", "All Files|*.*" } // Filter specifically for PNG
            );

            if (!string.IsNullOrEmpty(selectedFile))
            {
                CircleCenterImageFilePath = selectedFile; // Setter will save
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Error Browse for circle center image file: {ex.Message}"
            );
        }
    }

    // Command to clear background video
    [RelayCommand]
    private void ClearBackgroundVideo()
    {
        BackgroundVideoPath = string.Empty;
        _settingsService.Save();
    }

    // Command to clear background image
    [RelayCommand]
    private void ClearBackgroundImage()
    {
        BackgroundImagePath = string.Empty;
        _settingsService.Save();
    }

    [RelayCommand]
    private void ClearCircleCenterImage()
    {
        CircleCenterImageFilePath = string.Empty; // Setter will save
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
