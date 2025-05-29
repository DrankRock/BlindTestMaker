using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cogwheel;
using CommunityToolkit.Mvvm.ComponentModel;
using YoutubeDownloader.Core.AudioVisualisation;
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

    // Add property for the working directory
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
    public partial double XPosition { get; set; } = 0.0;

    [ObservableProperty]
    public partial double YPosition { get; set; } = 0.0;

    // Spherical-specific settings
    [ObservableProperty]
    public partial double SphereDiameter { get; set; } = 1.0;

    // Waveform-specific settings
    [ObservableProperty]
    public partial double WaveAmplitude { get; set; } = 1.0;

    [ObservableProperty]
    public partial double WaveFrequency { get; set; } = 1.0;

    // Spectrum bars settings
    [ObservableProperty]
    public partial int BarCount { get; set; } = 50;

    [ObservableProperty]
    public partial double BarSpacing { get; set; } = 1.0;

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

    public override void Save()
    {
        // Clear the cookies if they are not supposed to be persisted
        var lastAuthCookies = LastAuthCookies;
        if (!IsAuthPersisted)
            LastAuthCookies = null;

        base.Save();

        LastAuthCookies = lastAuthCookies;
    }
}

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
