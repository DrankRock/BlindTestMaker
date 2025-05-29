using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Cogwheel;
using CommunityToolkit.Mvvm.ComponentModel;
using YoutubeDownloader.Core.AudioVisualisation; // Ensure this namespace includes VisualizationMode, ColorMode
using YoutubeDownloader.Core.Downloading;
using YoutubeDownloader.Framework;
using Container = YoutubeExplode.Videos.Streams.Container;

namespace YoutubeDownloader.Services;

[ObservableObject]
public partial class SettingsService()
    : SettingsBase(
        Path.Combine(AppContext.BaseDirectory, "Settings.dat"),
        SerializerContext.Default
    )
{
    // Add this flag to prevent recursive saves during loading
    private bool _isLoading = false;
    private bool _isInitialized = false;

    [ObservableProperty]
    public partial bool IsUkraineSupportMessageEnabled { get; set; } = true;

    [ObservableProperty]
    public partial ThemeVariant Theme { get; set; }

    [ObservableProperty]
    public partial bool IsAutoUpdateEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool IsAuthPersisted { get; set; } = true;

    [ObservableProperty]
    public partial bool ShouldInjectLanguageSpecificAudioStreams { get; set; } = true;

    [ObservableProperty]
    public partial bool ShouldInjectSubtitles { get; set; } = true;

    [ObservableProperty]
    public partial bool ShouldInjectTags { get; set; } = true;

    [ObservableProperty]
    public partial bool ShouldSkipExistingFiles { get; set; }

    [ObservableProperty]
    public partial string FileNameTemplate { get; set; } = "$title";

    [ObservableProperty]
    public partial int ParallelLimit { get; set; } = 2;

    [ObservableProperty]
    public partial IReadOnlyList<Cookie>? LastAuthCookies { get; set; }

    [ObservableProperty]
    [JsonConverter(typeof(ContainerJsonConverter))]
    public partial Container LastContainer { get; set; } = Container.Mp4;

    [ObservableProperty]
    public partial VideoQualityPreference LastVideoQualityPreference { get; set; } =
        VideoQualityPreference.Highest;

    [ObservableProperty]
    public partial string? LastWorkingDirectory { get; set; }

    #region video generation
    [ObservableProperty]
    public partial VisualizationMode VisualizationMode { get; set; } =
        VisualizationMode.BasicWaveform;

    [ObservableProperty]
    public partial ColorMode ColorMode { get; set; } = ColorMode.Rainbow;

    [ObservableProperty]
    public partial int IntervalBetweenVideos { get; set; } = 0;

    [ObservableProperty]
    public partial string? BackgroundVideoPath { get; set; }

    [ObservableProperty]
    public partial string? BackgroundImagePath { get; set; }

    // Common visualization settings
    [ObservableProperty]
    public partial double ZoomLevel { get; set; } = 1.0;

    [ObservableProperty]
    public partial double XPosition { get; set; } = 0.0; // Global X offset (-1 to 1)

    [ObservableProperty]
    public partial double YPosition { get; set; } = 0.0; // Global Y offset (-1 to 1)

    // Base settings for circular visualizations
    [ObservableProperty]
    public partial float BaseRadius { get; set; } = 0.25f; // General base radius for circular items (e.g., percentage of min screen dim)

    // Spherical-specific settings
    [ObservableProperty]
    public partial double SphereDiameter { get; set; } = 1.0;

    // Waveform-specific settings (used also for amplitude scaling in other visualizers)
    [ObservableProperty]
    public partial double WaveAmplitude { get; set; } = 1.0; // General amplitude/intensity factor

    [ObservableProperty]
    public partial double WaveFrequency { get; set; } = 1.0; // Specific to waveform type visualizers

    [ObservableProperty]
    public partial float LineThickness { get; set; } = 3f; // For line-based visualizers like CircularWave

    // Circular Waveform-specific
    [ObservableProperty]
    public partial string? CircleCenterFilePath { get; set; } // Also used by CircularSpectrumBars

    // Spectrum bars settings (some are for linear, some can be adapted or new ones for circular)
    [ObservableProperty]
    public partial int BarCount { get; set; } = 64; // Used by both linear and circular spectrums

    [ObservableProperty]
    public partial double BarSpacing { get; set; } = 1.0; // Primarily for linear spectrum bars

    [ObservableProperty]
    public partial bool SpectrumLogarithmicScale { get; set; } = true; // For all spectrum types

    // --- New Settings for CircularSpectrumBars ---
    [ObservableProperty]
    public partial float CircularSpectrumBarFillRatio { get; set; } = 0.85f;

    [ObservableProperty]
    public partial float CircularSpectrumBarHeightScaleFactor { get; set; } = 1.2f;

    [ObservableProperty]
    public partial float CircularSpectrumMaxBarHeightRatio { get; set; } = 0.80f;

    [ObservableProperty]
    public partial bool CircularSpectrumMirrorBars { get; set; } = false;

    [ObservableProperty]
    public partial bool CircularSpectrumEnableGlow { get; set; } = true;

    [ObservableProperty]
    public partial float CircularSpectrumGlowIntensity { get; set; } = 70f; // Alpha 0-255

    [ObservableProperty]
    public partial float CircularSpectrumGlowOffset { get; set; } = 2.5f;

    [ObservableProperty]
    public partial float CircularSpectrumGlowAngularSpread { get; set; } = 0.015f; // Radians

    // Particle settings
    [ObservableProperty]
    public partial int ParticleCount { get; set; } = 500;

    [ObservableProperty]
    public partial double ParticleSpeed { get; set; } = 1.0;

    // Kaleidoscope settings
    [ObservableProperty]
    public partial int KaleidoscopeSegments { get; set; } = 6;

    [ObservableProperty]
    public partial double RotationSpeed { get; set; } = 1.0;

    // DNA Helix settings
    [ObservableProperty]
    public partial double HelixRadius { get; set; } = 0.5;

    [ObservableProperty]
    public partial double HelixPitch { get; set; } = 1.0;

    // Aurora settings
    [ObservableProperty]
    public partial double AuroraIntensity { get; set; } = 1.0;

    [ObservableProperty]
    public partial double AuroraFlowSpeed { get; set; } = 1.0;

    #endregion

    // Initialize the settings service
    public void Initialize()
    {
        if (_isInitialized)
            return;

        // Load existing settings first
        try
        {
            _isLoading = true;
            Load();
        }
        catch
        {
            // If loading fails, we'll use defaults
        }
        finally
        {
            _isLoading = false;
        }

        // Set up auto-save on property changes
        PropertyChanged += (sender, args) =>
        {
            // Don't save during loading to avoid overwriting with defaults
            if (!_isLoading && !string.IsNullOrEmpty(args.PropertyName))
            {
                // Delay the save slightly to batch multiple rapid changes
                _ = Task.Delay(100).ContinueWith(_ => Save());
            }
        };

        _isInitialized = true;
    }

    public override void Save()
    {
        // Clear the cookies if they are not supposed to be persisted
        var lastAuthCookies = LastAuthCookies;
        if (!IsAuthPersisted)
            LastAuthCookies = null;

        base.Save();

        LastAuthCookies = lastAuthCookies;
    }

    // Add a method to manually trigger save (useful for immediate saves)
    public void SaveNow()
    {
        if (!_isLoading)
        {
            Save();
        }
    }
}

// ContainerJsonConverter and SerializerContext remain unchanged
public partial class SettingsService
{
    private class ContainerJsonConverter : JsonConverter<Container>
    {
        public override Container Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            Container? result = null;

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (
                        reader.TokenType == JsonTokenType.PropertyName
                        && reader.GetString() == "Name"
                        && reader.Read()
                        && reader.TokenType == JsonTokenType.String
                    )
                    {
                        var name = reader.GetString();
                        if (!string.IsNullOrWhiteSpace(name))
                            result = new Container(name);
                    }
                }
            }

            return result
                ?? throw new InvalidOperationException(
                    $"Invalid JSON for type '{typeToConvert.FullName}'."
                );
        }

        public override void Write(
            Utf8JsonWriter writer,
            Container value,
            JsonSerializerOptions options
        )
        {
            writer.WriteStartObject();
            writer.WriteString("Name", value.Name);
            writer.WriteEndObject();
        }
    }
}

public partial class SettingsService
{
    [JsonSerializable(typeof(SettingsService))]
    private partial class SerializerContext : JsonSerializerContext;
}
