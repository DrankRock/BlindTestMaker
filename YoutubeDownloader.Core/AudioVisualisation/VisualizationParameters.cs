using System;
using System.Collections.Generic;

namespace YoutubeDownloader.Core.AudioVisualisation
{
    /// <summary>
    /// Base class for visualization parameters
    /// </summary>
    public abstract class VisualizationParameters
    {
        public virtual void SetDefaults() { }
    }

    /// <summary>
    /// Parameters for Basic Waveform visualization
    /// </summary>
    public class BasicWaveformParameters : VisualizationParameters
    {
        public float WaveHeight { get; set; } = 0.25f; // Percentage of screen height (0.1 to 1.0)
        public float LineThickness { get; set; } = 2f;
        public bool EnableGlow { get; set; } = false;
        public float VerticalPosition { get; set; } = 0.5f; // 0 = top, 1 = bottom

        public override void SetDefaults()
        {
            WaveHeight = 0.25f;
            LineThickness = 2f;
            EnableGlow = false;
            VerticalPosition = 0.5f;
        }
    }

    /// <summary>
    /// Parameters for Circular Wave visualization
    /// </summary>
    public class CircularWaveParameters : VisualizationParameters
    {
        public float CenterX { get; set; } = 0.5f; // Percentage of screen width (0 to 1)
        public float CenterY { get; set; } = 0.5f; // Percentage of screen height (0 to 1)
        public float BaseRadius { get; set; } = 0.25f; // Percentage of min(width, height)
        public float MaxRadiusMultiplier { get; set; } = 1.0f; // How much the wave can expand
        public int SamplePoints { get; set; } = 360; // Number of points around the circle
        public float LineThickness { get; set; } = 3f;
        public bool DrawMultipleRings { get; set; } = false;
        public int RingCount { get; set; } = 3;
        public string CircleCenterFilePath { get; set; } = "";

        public override void SetDefaults()
        {
            CenterX = 0.5f;
            CenterY = 0.5f;
            BaseRadius = 0.25f;
            MaxRadiusMultiplier = 1.0f;
            SamplePoints = 360;
            LineThickness = 3f;
            DrawMultipleRings = false;
            RingCount = 3;
            CircleCenterFilePath = "";
        }
    }

    /// <summary>
    /// Parameters for Spherical Pulse visualization
    /// </summary>
    public class SphericalPulseParameters : VisualizationParameters
    {
        public float CenterX { get; set; } = 0.5f;
        public float CenterY { get; set; } = 0.5f;
        public int MaxCircles { get; set; } = 20;
        public float BaseRadius { get; set; } = 50f; // Base radius in pixels
        public float RadiusGrowthRate { get; set; } = 20f; // Pixels per history frame
        public float AmplitudeMultiplier { get; set; } = 500f;
        public float AlphaFalloff { get; set; } = 1.0f; // How quickly older circles fade

        public override void SetDefaults()
        {
            CenterX = 0.5f;
            CenterY = 0.5f;
            MaxCircles = 20;
            BaseRadius = 50f;
            RadiusGrowthRate = 20f;
            AmplitudeMultiplier = 500f;
            AlphaFalloff = 1.0f;
        }
    }

    /// <summary>
    /// Parameters for Spectrum Bars visualization
    /// </summary>
    public class SpectrumBarsParameters : VisualizationParameters
    {
        public int BarCount { get; set; } = 64;
        public float BarSpacing { get; set; } = 2f; // Pixels between bars
        public float MaxBarHeight { get; set; } = 0.8f; // Percentage of screen height
        public float AmplitudeMultiplier { get; set; } = 10f;
        public bool EnableGlow { get; set; } = true;
        public float GlowIntensity { get; set; } = 50f; // Alpha value for glow
        public bool MirrorBars { get; set; } = false; // Draw bars from center
        public bool LogarithmicScale { get; set; } = true; // Use log scale for frequency distribution

        public override void SetDefaults()
        {
            BarCount = 64;
            BarSpacing = 2f;
            MaxBarHeight = 0.8f;
            AmplitudeMultiplier = 10f;
            EnableGlow = true;
            GlowIntensity = 50f;
            MirrorBars = false;
            LogarithmicScale = true;
        }
    }

    public class CircularSpectrumBarsParameters : VisualizationParameters
    {
        // --- Core Spectrum Data Processing ---
        /// <summary>
        /// Number of bars to display in the circle.
        /// </summary>
        public int BarCount { get; set; }

        /// <summary>
        /// Multiplier for the FFT magnitude to determine bar height.
        /// </summary>
        public float AmplitudeMultiplier { get; set; }

        /// <summary>
        /// Use a logarithmic scale for frequency distribution across bars,
        /// providing better representation of lower frequencies.
        /// </summary>
        public bool LogarithmicScale { get; set; }

        // --- Circular Arrangement & Base ---
        /// <summary>
        /// Horizontal center of the circular spectrum (percentage of screen width, 0.0 to 1.0).
        /// </summary>
        public float CenterX { get; set; }

        /// <summary>
        /// Vertical center of the circular spectrum (percentage of screen height, 0.0 to 1.0).
        /// </summary>
        public float CenterY { get; set; }

        /// <summary>
        /// Optional file path to a PNG image to be displayed at the center.
        /// If provided, its dimensions can influence the baseRadius.
        /// </summary>
        public string CircleCenterFilePath { get; set; }

        /// <summary>
        /// Fallback base radius if no CircleCenterFilePath is provided or image fails to load.
        /// Defined as a percentage of the smaller screen dimension (width or height).
        /// This is the radius of the inner circle from which bars emanate.
        /// </summary>
        public float BaseRadiusPercentage { get; set; }

        // --- Bar Appearance in Circular Layout ---
        /// <summary>
        /// Ratio of the angular space each bar occupies within its allocated slot (0.0 to 1.0).
        /// E.g., 0.8 means 80% bar, 20% combined spacing around it.
        /// </summary>
        public float BarFillRatio { get; set; }

        /// <summary>
        /// Factor to scale the bar's height based on FFT magnitude, relative to the baseRadius.
        /// E.g., if baseRadius is 100, magnitude is 0.5, factor is 1.0, then height contribution is 50.
        /// </summary>
        public float BarHeightScaleFactor { get; set; }

        /// <summary>
        /// Maximum height of a bar as a ratio of the baseRadius.
        /// E.g., 0.75 means a bar can be at most 75% of the baseRadius in length.
        /// </summary>
        public float MaxBarHeightRatio { get; set; }

        // --- Visual Effects ---
        /// <summary>
        /// If true, draws bars extending both outwards and inwards from the baseRadius.
        /// </summary>
        public bool MirrorBars { get; set; }

        /// <summary>
        /// Enables a glow effect around the bars.
        /// </summary>
        public bool EnableGlow { get; set; }

        /// <summary>
        /// Alpha intensity of the glow effect (0 to 255).
        /// </summary>
        public int GlowIntensity { get; set; } // Will be cast to int for Color.FromArgb

        /// <summary>
        /// Radial offset for the glow polygon from the actual bar edges (in units consistent with radius).
        /// </summary>
        public float GlowOffset { get; set; }

        /// <summary>
        /// Additional angular spread (in radians) for the glow polygon on each side of the bar.
        /// </summary>
        public float GlowAngularSpread { get; set; }
        public bool UseContinuousWaves { get; set; } = true;

        public override void SetDefaults()
        {
            // Core Spectrum
            BarCount = 64;
            AmplitudeMultiplier = 15f; // Adjusted as bar heights are now relative to baseRadius
            LogarithmicScale = true;

            // Circular Arrangement
            CenterX = 0.5f;
            CenterY = 0.5f;
            CircleCenterFilePath = "";
            BaseRadiusPercentage = 0.20f; // Inner radius for bars to start from (20% of min screen dimension)

            // Bar Appearance
            BarFillRatio = 0.85f; // 85% bar, 15% total spacing
            BarHeightScaleFactor = 1.2f; // How much FFT magnitude contributes to bar length relative to baseRadius
            MaxBarHeightRatio = 0.80f; // Max bar length is 80% of baseRadius

            // Effects
            MirrorBars = false;
            EnableGlow = true;
            GlowIntensity = 70; // Alpha for glow (0-255)
            GlowOffset = 2.5f; // Radial expansion for glow
            GlowAngularSpread = 0.015f; // Radians for angular glow expansion (per side)

            UseContinuousWaves = true;
        }
    }

    /// <summary>
    /// Parameters for Particle Flow visualization
    /// </summary>
    public class ParticleFlowParameters : VisualizationParameters
    {
        public int MaxParticles { get; set; } = 2000;
        public float SpawnThreshold { get; set; } = 0.1f; // Amplitude threshold to spawn particles
        public int SpawnRate { get; set; } = 20; // Max particles per frame
        public float BaseParticleSize { get; set; } = 2f;
        public float MaxParticleSize { get; set; } = 7f;
        public float VelocityMultiplier { get; set; } = 20f;
        public float LifeDecayRate { get; set; } = 0.01f;
        public float DampingFactor { get; set; } = 0.98f;
        public bool EnableAudioForces { get; set; } = true;
        public float ForceMultiplier { get; set; } = 0.1f;

        public override void SetDefaults()
        {
            MaxParticles = 2000;
            SpawnThreshold = 0.1f;
            SpawnRate = 20;
            BaseParticleSize = 2f;
            MaxParticleSize = 7f;
            VelocityMultiplier = 20f;
            LifeDecayRate = 0.01f;
            DampingFactor = 0.98f;
            EnableAudioForces = true;
            ForceMultiplier = 0.1f;
        }
    }

    /// <summary>
    /// Parameters for Kaleidoscope Wave visualization
    /// </summary>
    public class KaleidoscopeWaveParameters : VisualizationParameters
    {
        public int Segments { get; set; } = 8;
        public float CenterX { get; set; } = 0.5f;
        public float CenterY { get; set; } = 0.5f;
        public int PointsPerSegment { get; set; } = 100;
        public float BaseRadius { get; set; } = 50f;
        public float RadiusGrowthRate { get; set; } = 1.0f; // How quickly radius increases along the spiral
        public float WaveAmplitude { get; set; } = 100f;
        public float SpiralTightness { get; set; } = 0.1f; // Angle increment per point
        public float LineThickness { get; set; } = 2f;

        public override void SetDefaults()
        {
            Segments = 8;
            CenterX = 0.5f;
            CenterY = 0.5f;
            PointsPerSegment = 100;
            BaseRadius = 50f;
            RadiusGrowthRate = 1.0f;
            WaveAmplitude = 100f;
            SpiralTightness = 0.1f;
            LineThickness = 2f;
        }
    }

    /// <summary>
    /// Parameters for DNA Helix visualization
    /// </summary>
    public class DNAHelixParameters : VisualizationParameters
    {
        public float CenterX { get; set; } = 0.5f;
        public int HelixPoints { get; set; } = 200; // Number of points along the helix
        public float HelixRadius { get; set; } = 50f; // Base radius
        public float RadiusAmplitudeMultiplier { get; set; } = 0.125f; // How much audio affects radius
        public float HelixSpeed { get; set; } = 0.05f; // Rotation speed
        public float WaveFrequency { get; set; } = 0.02f; // How tight the helix is
        public float NodeSize { get; set; } = 10f; // Size of the DNA nodes
        public bool DrawConnections { get; set; } = true;
        public int ConnectionFrequency { get; set; } = 20; // Draw connection every N points

        public override void SetDefaults()
        {
            CenterX = 0.5f;
            HelixPoints = 200;
            HelixRadius = 50f;
            RadiusAmplitudeMultiplier = 0.125f;
            HelixSpeed = 0.05f;
            WaveFrequency = 0.02f;
            NodeSize = 10f;
            DrawConnections = true;
            ConnectionFrequency = 20;
        }
    }

    /// <summary>
    /// Parameters for Aurora visualization
    /// </summary>
    public class AuroraParameters : VisualizationParameters
    {
        public int MinBands { get; set; } = 3;
        public int MaxBands { get; set; } = 10;
        public float BandSpread { get; set; } = 0.5f; // How spread out the bands are (0.1 to 1.0)
        public float WaveAmplitude { get; set; } = 0.1f; // Base wave height
        public float AudioAmplitude { get; set; } = 0.15f; // How much audio affects height
        public float WaveSpeed { get; set; } = 0.01f; // Animation speed
        public float IntensityBase { get; set; } = 0.2f;
        public float IntensityMultiplier { get; set; } = 0.4f;
        public bool EnableGradient { get; set; } = true;

        public override void SetDefaults()
        {
            MinBands = 3;
            MaxBands = 10;
            BandSpread = 0.5f;
            WaveAmplitude = 0.1f;
            AudioAmplitude = 0.15f;
            WaveSpeed = 0.01f;
            IntensityBase = 0.2f;
            IntensityMultiplier = 0.4f;
            EnableGradient = true;
        }
    }

    /// <summary>
    /// Factory class to create default parameters for each visualization mode
    /// </summary>
    public static class VisualizationParametersFactory
    {
        public static VisualizationParameters CreateDefault(VisualizationMode mode)
        {
            VisualizationParameters parameters = mode switch
            {
                VisualizationMode.BasicWaveform => new BasicWaveformParameters(),
                VisualizationMode.CircularWave => new CircularWaveParameters(),
                VisualizationMode.SphericalPulse => new SphericalPulseParameters(),
                VisualizationMode.SpectrumBars => new SpectrumBarsParameters(),
                VisualizationMode.CircularSpectrumBars => new CircularSpectrumBarsParameters(),
                VisualizationMode.ParticleFlow => new ParticleFlowParameters(),
                VisualizationMode.KaleidoscopeWave => new KaleidoscopeWaveParameters(),
                VisualizationMode.DNA_Helix => new DNAHelixParameters(),
                VisualizationMode.Aurora => new AuroraParameters(),
                _ => throw new ArgumentException($"Unknown visualization mode: {mode}"),
            };

            parameters.SetDefaults();
            return parameters;
        }
    }
}
