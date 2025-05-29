using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YoutubeDownloader.Core.AudioVisualisation;
using YoutubeDownloader.Services;

namespace YoutubeDownloader.Converters
{
    public static class VisualizationParametersFromSettings
    {
        public static VisualizationParameters CreateFromSettings(SettingsService settings)
        {
            var mode = settings.VisualizationMode;
            VisualizationParameters parameters = mode switch
            {
                VisualizationMode.BasicWaveform => CreateBasicWaveformParameters(settings),
                VisualizationMode.CircularWave => CreateCircularWaveParameters(settings),
                VisualizationMode.SphericalPulse => CreateSphericalPulseParameters(settings),
                VisualizationMode.SpectrumBars => CreateSpectrumBarsParameters(settings),
                VisualizationMode.ParticleFlow => CreateParticleFlowParameters(settings),
                VisualizationMode.KaleidoscopeWave => CreateKaleidoscopeWaveParameters(settings),
                VisualizationMode.DNA_Helix => CreateDNAHelixParameters(settings),
                VisualizationMode.Aurora => CreateAuroraParameters(settings),
                _ => throw new ArgumentException($"Unknown visualization mode: {mode}"),
            };

            return parameters;
        }

        private static BasicWaveformParameters CreateBasicWaveformParameters(
            SettingsService settings
        )
        {
            return new BasicWaveformParameters
            {
                WaveHeight = (float)(settings.WaveAmplitude * 0.25f), // Convert amplitude to wave height
                LineThickness = 2f, // Could be made configurable
                EnableGlow = false, // Could be made configurable
                VerticalPosition = 0.5f + (float)settings.YPosition, // Center position + offset
            };
        }

        private static CircularWaveParameters CreateCircularWaveParameters(SettingsService settings)
        {
            return new CircularWaveParameters
            {
                CenterX = 0.5f + (float)settings.XPosition,
                CenterY = 0.5f + (float)settings.YPosition,
                BaseRadius = (float)(0.25f * settings.ZoomLevel),
                MaxRadiusMultiplier = (float)settings.WaveAmplitude,
                SamplePoints = 360, // Could be made configurable
                LineThickness = 3f,
                DrawMultipleRings = false, // Could be made configurable
                RingCount = 3,
            };
        }

        private static SphericalPulseParameters CreateSphericalPulseParameters(
            SettingsService settings
        )
        {
            return new SphericalPulseParameters
            {
                CenterX = 0.5f + (float)settings.XPosition,
                CenterY = 0.5f + (float)settings.YPosition,
                MaxCircles = 20,
                BaseRadius = (float)(50f * settings.SphereDiameter),
                RadiusGrowthRate = 20f,
                AmplitudeMultiplier = (float)(500f * settings.WaveAmplitude),
                AlphaFalloff = 1.0f,
            };
        }

        private static SpectrumBarsParameters CreateSpectrumBarsParameters(SettingsService settings)
        {
            return new SpectrumBarsParameters
            {
                BarCount = settings.BarCount,
                BarSpacing = (float)settings.BarSpacing,
                MaxBarHeight = 0.8f,
                AmplitudeMultiplier = (float)(10f * settings.WaveAmplitude),
                EnableGlow = true,
                GlowIntensity = 50f,
                MirrorBars = false, // Could be made configurable
                LogarithmicScale = true,
            };
        }

        private static ParticleFlowParameters CreateParticleFlowParameters(SettingsService settings)
        {
            return new ParticleFlowParameters
            {
                MaxParticles = settings.ParticleCount,
                SpawnThreshold = 0.1f,
                SpawnRate = 20,
                BaseParticleSize = 2f,
                MaxParticleSize = 7f,
                VelocityMultiplier = (float)(20f * settings.ParticleSpeed),
                LifeDecayRate = 0.01f,
                DampingFactor = 0.98f,
                EnableAudioForces = true,
                ForceMultiplier = 0.1f,
            };
        }

        private static KaleidoscopeWaveParameters CreateKaleidoscopeWaveParameters(
            SettingsService settings
        )
        {
            return new KaleidoscopeWaveParameters
            {
                Segments = settings.KaleidoscopeSegments,
                CenterX = 0.5f + (float)settings.XPosition,
                CenterY = 0.5f + (float)settings.YPosition,
                PointsPerSegment = 100,
                BaseRadius = (float)(50f * settings.ZoomLevel),
                RadiusGrowthRate = 1.0f,
                WaveAmplitude = (float)(100f * settings.WaveAmplitude),
                SpiralTightness = 0.1f,
                LineThickness = 2f,
            };
        }

        private static DNAHelixParameters CreateDNAHelixParameters(SettingsService settings)
        {
            return new DNAHelixParameters
            {
                CenterX = 0.5f + (float)settings.XPosition,
                HelixPoints = 200,
                HelixRadius = (float)(50f * settings.HelixRadius),
                RadiusAmplitudeMultiplier = (float)(0.125f * settings.WaveAmplitude),
                HelixSpeed = (float)(0.05f * settings.RotationSpeed),
                WaveFrequency = (float)(0.02f * settings.HelixPitch),
                NodeSize = 10f,
                DrawConnections = true,
                ConnectionFrequency = 20,
            };
        }

        private static AuroraParameters CreateAuroraParameters(SettingsService settings)
        {
            return new AuroraParameters
            {
                MinBands = 3,
                MaxBands = 10,
                BandSpread = 0.5f,
                WaveAmplitude = (float)(0.1f * settings.WaveAmplitude),
                AudioAmplitude = (float)(0.15f * settings.AuroraIntensity),
                WaveSpeed = (float)(0.01f * settings.AuroraFlowSpeed),
                IntensityBase = 0.2f,
                IntensityMultiplier = (float)(0.4f * settings.AuroraIntensity),
                EnableGradient = true,
            };
        }
    }
}
