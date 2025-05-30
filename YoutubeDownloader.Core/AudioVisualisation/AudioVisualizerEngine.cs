using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LibVLCSharp.Shared;
using NAudio.Dsp;
using NAudio.Wave;
using YoutubeDownloader.Core.Utils.Extensions;

namespace YoutubeDownloader.Core.AudioVisualisation
{
    public enum VisualizationMode
    {
        BasicWaveform,
        CircularWave,
        SphericalPulse,
        SpectrumBars,
        CircularSpectrumBars,
        ParticleFlow,
        KaleidoscopeWave,
        DNA_Helix,
        Aurora,
    }

    public enum ColorMode
    {
        Rainbow,
        FrequencyBased,
        AmplitudeBased,
        PsychedelicFlow,
        DeepBassReactive,
        HighFreqSparkle,
        EmotionalMapping,
    }

    public enum VideoCodec
    {
        H264,
        H265,
        VP9,
    }

    public class AudioVisualizerEngine
    {
        public readonly int _width;
        public readonly int _height;
        public readonly int _fps;
        private readonly string _outputPath;
        private readonly LibVLC _libVLC;
        public float[] _fftBuffer;
        private NAudio.Dsp.Complex[] _fftComplex;
        private readonly object _sampleLock = new object();
        public readonly Random _random = new Random();
        public int _frameCount = 0;
        public float _smoothBass = 0f;
        public float _smoothMid = 0f;
        public float _smoothHigh = 0f;
        private readonly Queue<float[]> _historyBuffer = new Queue<float[]>();
        private readonly int _historySize = 30;
        public List<float> _audioSamples;

        // NEW: Constants for audio processing (previously from the non-existent VisualizationConstants)
        private const float BASS_BOOST_THRESHOLD = 0.15f; // Example: Affects first 15% of bars
        private const float BASS_AMPLIFICATION = 1.8f; // Example: 1.8x boost in the bass band
        private const float TREBLE_BOOST_THRESHOLD = 0.65f; // Example: Affects last 35% of bars
        private const float TREBLE_AMPLIFICATION = 1.6f;

        // Particle system for advanced effects
        private List<Particle> _particles = new List<Particle>();

        public AudioVisualizerEngine(
            int width = 1920,
            int height = 1080,
            int fps = 60,
            string outputPath = "output.mp4"
        )
        {
            DebugWrite.Line(
                $"AudioVisualizerEngine.Constructor - Initializing with Width: {width}, Height: {height}, FPS: {fps}, OutputPath: {outputPath}"
            );
            _width = width;
            _height = height;
            _fps = fps;
            _outputPath = outputPath;
            DebugWrite.Line("AudioVisualizerEngine.Constructor - Initializing LibVLC.");
            _libVLC = new LibVLC();
            _fftBuffer = new float[2048];
            _fftComplex = new NAudio.Dsp.Complex[2048];
            _audioSamples = new List<float>();
            DebugWrite.Line("AudioVisualizerEngine.Constructor - Initialization complete.");
        }

        private VisualizationParameters _visualizationParameters;

        // Add these constants at the top of AudioVisualizerEngine class
        private const int PROGRESS_LOG_INTERVAL = 60; // Log every 60 frames (1 second at 60fps)
        private const int DETAILED_LOG_INTERVAL = 600; // Detailed logs every 600 frames (10 seconds)

        public async Task CreateVisualization(
            string mp3Path,
            VisualizationMode mode,
            ColorMode colorMode,
            VisualizationParameters parameters = null
        )
        {
            DebugWrite.Line(
                $"AudioVisualizerEngine.CreateVisualization - Starting visualization for MP3: {mp3Path}, Mode: {mode}, ColorMode: {colorMode}"
            );

            _visualizationParameters =
                parameters ?? VisualizationParametersFactory.CreateDefault(mode);

            await LoadAudioData(mp3Path);
            DebugWrite.Line("AudioVisualizerEngine.CreateVisualization - Audio data loaded.");

            var videoWriter = new VideoFileWriter();
            videoWriter.Open(_outputPath, _width, _height, _fps, VideoCodec.H264);
            DebugWrite.Line(
                $"AudioVisualizerEngine.CreateVisualization - Video writer opened for output: {_outputPath}"
            );

            var duration = GetAudioDuration(mp3Path);
            var totalFrames = (int)(duration * _fps);
            DebugWrite.Line(
                $"AudioVisualizerEngine.CreateVisualization - Audio duration: {duration:F1}s, Total frames: {totalFrames}"
            );

            if (mode == VisualizationMode.ParticleFlow || mode == VisualizationMode.Aurora)
            {
                InitializeParticles(1000);
            }

            DebugWrite.Line(
                "AudioVisualizerEngine.CreateVisualization - Starting frame generation..."
            );
            var startTime = DateTime.Now;

            for (int frame = 0; frame < totalFrames; frame++)
            {
                var bitmap = GenerateFrame(frame, totalFrames, mode, colorMode);
                videoWriter.WriteVideoFrame(bitmap);
                bitmap.Dispose();
                _frameCount++;

                // Progress logging every second (60 frames at 60fps)
                if (frame % PROGRESS_LOG_INTERVAL == 0 || frame == totalFrames - 1)
                {
                    var elapsed = DateTime.Now - startTime;
                    var progress = (float)frame / totalFrames * 100f;
                    var framesPerSecond = frame > 0 ? frame / elapsed.TotalSeconds : 0;
                    var estimatedTimeLeft =
                        framesPerSecond > 0
                            ? TimeSpan.FromSeconds((totalFrames - frame) / framesPerSecond)
                            : TimeSpan.Zero;

                    DebugWrite.Line(
                        $"PROGRESS: Frame {frame + 1:N0}/{totalFrames:N0} ({progress:F1}%) | "
                            + $"Speed: {framesPerSecond:F1} fps | "
                            + $"Elapsed: {elapsed:mm\\:ss} | "
                            + $"ETA: {estimatedTimeLeft:mm\\:ss}"
                    );
                }

                // Detailed logging every 10 seconds
                if (frame % DETAILED_LOG_INTERVAL == 0 && frame > 0)
                {
                    DebugWrite.Line(
                        $"DETAIL: Bass={_smoothBass:F3}, Mid={_smoothMid:F3}, High={_smoothHigh:F3} | "
                            + $"Particles={_particles.Count} | "
                            + $"Memory: {GC.GetTotalMemory(false) / 1024 / 1024} MB"
                    );
                }
            }

            var totalTime = DateTime.Now - startTime;
            DebugWrite.Line(
                $"COMPLETE: Frame generation finished in {totalTime:mm\\:ss} | "
                    + $"Average: {totalFrames / totalTime.TotalSeconds:F1} fps"
            );

            videoWriter.Close();
            await MergeAudioVideo(mp3Path, _outputPath);
            DebugWrite.Line(
                $"AudioVisualizerEngine.CreateVisualization - Visualization complete: {_outputPath}"
            );
        }

        public async Task LoadAudioData(string mp3Path)
        {
            DebugWrite.Line(
                $"AudioVisualizerEngine.LoadAudioData - Starting to load audio data from: {mp3Path}"
            );
            await Task.Run(() =>
            {
                DebugWrite.Line(
                    $"AudioVisualizerEngine.LoadAudioData - Task started for reading MP3: {mp3Path}"
                );
                using (var reader = new Mp3FileReader(mp3Path))
                {
                    DebugWrite.Line(
                        $"AudioVisualizerEngine.LoadAudioData - Mp3FileReader opened. Sample rate: {reader.WaveFormat.SampleRate}, Channels: {reader.WaveFormat.Channels}"
                    );
                    var sampleProvider = reader.ToSampleProvider();
                    var samples = new List<float>();
                    var buffer = new float[1024];
                    int samplesRead;
                    int totalSamplesRead = 0;

                    DebugWrite.Line(
                        "AudioVisualizerEngine.LoadAudioData - Starting to read samples."
                    );
                    while ((samplesRead = sampleProvider.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        samples.AddRange(buffer.Take(samplesRead));
                        totalSamplesRead += samplesRead;
                        DebugWrite.Line(
                            $"AudioVisualizerEngine.LoadAudioData - Read {samplesRead} samples. Total samples in list: {samples.Count}"
                        );
                    }
                    DebugWrite.Line(
                        $"AudioVisualizerEngine.LoadAudioData - Finished reading samples. Total samples read from file: {totalSamplesRead}"
                    );
                    _audioSamples = samples;
                    DebugWrite.Line(
                        $"AudioVisualizerEngine.LoadAudioData - _audioSamples populated with {samples.Count} samples."
                    );
                }
            });
            DebugWrite.Line(
                $"AudioVisualizerEngine.LoadAudioData - Finished loading audio data from: {mp3Path}. Total samples: {_audioSamples.Count}"
            );
        }

        private System.Drawing.Bitmap GenerateFrame(
            int frameIndex,
            int totalFrames,
            VisualizationMode mode,
            ColorMode colorMode
        )
        {
            // Only log errors or first/last frames
            if (frameIndex == 0 || frameIndex == totalFrames - 1)
            {
                DebugWrite.Line(
                    $"AudioVisualizerEngine.GenerateFrame - Frame {frameIndex} / {totalFrames}. Mode: {mode}"
                );
            }

            Bitmap bitmap = new Bitmap(_width, _height);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.FillRectangle(new SolidBrush(Color.FromArgb(20, 0, 0, 0)), 0, 0, _width, _height);

                var samplesPerFrame = _audioSamples.Count / totalFrames;
                var startIndex = frameIndex * samplesPerFrame;
                var endIndex = Math.Min(startIndex + samplesPerFrame, _audioSamples.Count);

                if (startIndex < _audioSamples.Count)
                {
                    var currentSamplesLength = endIndex - startIndex;
                    if (currentSamplesLength <= 0)
                    {
                        return bitmap;
                    }

                    var currentSamples = new float[currentSamplesLength];
                    Array.Copy(
                        _audioSamples.ToArray(),
                        startIndex,
                        currentSamples,
                        0,
                        currentSamples.Length
                    );

                    PerformFFT(currentSamples);
                    UpdateFrequencyBands();
                    UpdateHistory(currentSamples);

                    switch (mode)
                    {
                        case VisualizationMode.BasicWaveform:
                            DrawBasicWaveform(
                                g,
                                currentSamples,
                                colorMode,
                                _visualizationParameters as BasicWaveformParameters
                            );
                            break;
                        case VisualizationMode.CircularWave:
                            DrawCircularWave(
                                g,
                                currentSamples,
                                colorMode,
                                _visualizationParameters as CircularWaveParameters
                            );
                            break;
                        case VisualizationMode.SphericalPulse:
                            DrawSphericalPulse(
                                g,
                                currentSamples,
                                colorMode,
                                _visualizationParameters as SphericalPulseParameters
                            );
                            break;
                        case VisualizationMode.SpectrumBars:
                            DrawSpectrumBars(
                                g,
                                colorMode,
                                _visualizationParameters as SpectrumBarsParameters
                            );
                            break;
                        case VisualizationMode.CircularSpectrumBars:
                            DrawCircularSpectrumBars(
                                g,
                                colorMode,
                                _visualizationParameters as CircularSpectrumBarsParameters
                            );
                            break;
                        case VisualizationMode.ParticleFlow:
                            DrawParticleFlow(
                                g,
                                currentSamples,
                                colorMode,
                                _visualizationParameters as ParticleFlowParameters
                            );
                            break;
                        case VisualizationMode.KaleidoscopeWave:
                            DrawKaleidoscopeWave(
                                g,
                                currentSamples,
                                colorMode,
                                _visualizationParameters as KaleidoscopeWaveParameters
                            );
                            break;
                        case VisualizationMode.DNA_Helix:
                            DrawDNAHelix(
                                g,
                                currentSamples,
                                colorMode,
                                _visualizationParameters as DNAHelixParameters
                            );
                            break;
                        case VisualizationMode.Aurora:
                            DrawAurora(
                                g,
                                currentSamples,
                                colorMode,
                                _visualizationParameters as AuroraParameters
                            );
                            break;
                    }
                }
            }
            return bitmap;
        }

        public void PerformFFT(float[] samples)
        {
            DebugWrite.Line(
                $"AudioVisualizerEngine.PerformFFT - Performing FFT on {samples.Length} samples."
            );
            if (samples.Length == 0)
            {
                DebugWrite.Line(
                    "AudioVisualizerEngine.PerformFFT - No samples to perform FFT on. Clearing _fftBuffer."
                );
                Array.Clear(_fftBuffer, 0, _fftBuffer.Length); // Clear previous FFT data if no new samples
                return;
            }

            int fftLength = _fftComplex.Length;
            DebugWrite.Line($"AudioVisualizerEngine.PerformFFT - FFT Complex Length: {fftLength}");

            for (int i = 0; i < fftLength; i++)
            {
                if (i < samples.Length)
                {
                    _fftComplex[i].X =
                        samples[i] * (float)FastFourierTransform.HammingWindow(i, fftLength);
                    _fftComplex[i].Y = 0;
                }
                else
                {
                    _fftComplex[i].X = 0; // Zero-padding
                    _fftComplex[i].Y = 0;
                }
            }
            DebugWrite.Line(
                "AudioVisualizerEngine.PerformFFT - FFT buffer prepared with Hamming window and zero-padding if necessary."
            );

            FastFourierTransform.FFT(true, (int)Math.Log(fftLength, 2), _fftComplex);
            DebugWrite.Line("AudioVisualizerEngine.PerformFFT - FFT executed.");

            for (int i = 0; i < _fftBuffer.Length / 2; i++)
            {
                float real = _fftComplex[i].X;
                float imaginary = _fftComplex[i].Y;
                _fftBuffer[i] = (float)Math.Sqrt(real * real + imaginary * imaginary);
                if (i < 5)
                    DebugWrite.Line(
                        $"AudioVisualizerEngine.PerformFFT - _fftBuffer[{i}] Magnitude: {_fftBuffer[i]} (Real: {real}, Imaginary: {imaginary})"
                    );
            }
            DebugWrite.Line(
                "AudioVisualizerEngine.PerformFFT - Magnitudes calculated and stored in _fftBuffer."
            );
        }

        public void UpdateFrequencyBands()
        {
            DebugWrite.Line(
                "AudioVisualizerEngine.UpdateFrequencyBands - Updating frequency bands."
            );
            float bass = 0,
                mid = 0,
                high = 0;
            int bassEnd = _fftBuffer.Length / 8; // Example: 2048/8 = 256
            int midEnd = _fftBuffer.Length / 4; // Example: 2048/4 = 512
            // Max useful index is _fftBuffer.Length / 2 - 1. For 2048, it's 1023.

            DebugWrite.Line(
                $"AudioVisualizerEngine.UpdateFrequencyBands - bassEnd index: {bassEnd}, midEnd index: {midEnd}, fftBuffer half length: {_fftBuffer.Length / 2}"
            );

            for (int i = 0; i < bassEnd; i++)
                bass += _fftBuffer[i];
            for (int i = bassEnd; i < midEnd; i++)
                mid += _fftBuffer[i];
            for (int i = midEnd; i < _fftBuffer.Length / 2; i++)
                high += _fftBuffer[i];

            DebugWrite.Line(
                $"AudioVisualizerEngine.UpdateFrequencyBands - Raw sums: Bass={bass}, Mid={mid}, High={high}"
            );

            float bassAvg = (bassEnd > 0) ? bass / bassEnd : 0;
            float midAvg = (midEnd - bassEnd > 0) ? mid / (midEnd - bassEnd) : 0;
            float highAvg =
                (_fftBuffer.Length / 2 - midEnd > 0) ? high / (_fftBuffer.Length / 2 - midEnd) : 0;

            DebugWrite.Line(
                $"AudioVisualizerEngine.UpdateFrequencyBands - Raw averages: BassAvg={bassAvg}, MidAvg={midAvg}, HighAvg={highAvg}"
            );

            _smoothBass = Lerp(_smoothBass, bassAvg, 0.3f);
            _smoothMid = Lerp(_smoothMid, midAvg, 0.3f);
            _smoothHigh = Lerp(_smoothHigh, highAvg, 0.3f);

            DebugWrite.Line(
                $"AudioVisualizerEngine.UpdateFrequencyBands - Smoothed values: _smoothBass={_smoothBass}, _smoothMid={_smoothMid}, _smoothHigh={_smoothHigh}"
            );
        }

        private void UpdateHistory(float[] samples)
        {
            DebugWrite.Line(
                $"AudioVisualizerEngine.UpdateHistory - Updating history with {samples.Length} samples."
            );
            _historyBuffer.Enqueue((float[])samples.Clone()); // Enqueue a copy
            DebugWrite.Line(
                $"AudioVisualizerEngine.UpdateHistory - Enqueued. History buffer size: {_historyBuffer.Count}"
            );
            if (_historyBuffer.Count > _historySize)
            {
                _historyBuffer.Dequeue();
                DebugWrite.Line(
                    $"AudioVisualizerEngine.UpdateHistory - Dequeued. History buffer size: {_historyBuffer.Count}"
                );
            }
            DebugWrite.Line("AudioVisualizerEngine.UpdateHistory - History update complete.");
        }

        public Color GetColor(ColorMode mode, float intensity, float position = 0)
        {
            intensity = Math.Max(0f, Math.Min(1f, intensity));

            switch (mode)
            {
                case ColorMode.Rainbow:
                    return HSVtoRGB((position + _frameCount * 0.001f) % 1f, 1f, intensity);
                case ColorMode.FrequencyBased:
                    float hue = (_smoothBass * 0.1f + _smoothMid * 0.5f + _smoothHigh * 0.9f);
                    hue = hue - (float)Math.Floor(hue);
                    return HSVtoRGB(hue, 0.8f, intensity);
                case ColorMode.AmplitudeBased:
                    return Color.FromArgb(
                        (int)(intensity * 255),
                        (int)(intensity * 128 + 127),
                        (int)(intensity * 128),
                        (int)((1f - intensity) * 255)
                    );
                case ColorMode.PsychedelicFlow:
                    float r = (float)Math.Sin(_frameCount * 0.02f + position * 3) * 0.5f + 0.5f;
                    float g = (float)Math.Sin(_frameCount * 0.03f + position * 4) * 0.5f + 0.5f;
                    float b = (float)Math.Sin(_frameCount * 0.01f + position * 5) * 0.5f + 0.5f;
                    return Color.FromArgb(
                        (int)(intensity * 255),
                        (int)(r * 255),
                        (int)(g * 255),
                        (int)(b * 255)
                    );
                case ColorMode.DeepBassReactive:
                    float bassIntensity = Math.Min(_smoothBass * 10f, 1f);
                    return Color.FromArgb(
                        (int)(intensity * 255),
                        (int)(bassIntensity * 100),
                        0,
                        (int)(bassIntensity * 255)
                    );
                case ColorMode.HighFreqSparkle:
                    float highIntensity = Math.Min(_smoothHigh * 20f, 1f);
                    return Color.FromArgb(
                        (int)(intensity * 255),
                        (int)(highIntensity * 255),
                        (int)(highIntensity * 200),
                        (int)(highIntensity * 100)
                    );
                case ColorMode.EmotionalMapping:
                    float sumFreq = _smoothBass + _smoothMid + _smoothHigh + 0.001f;
                    float emotional = _smoothBass / sumFreq;
                    float emotionalHue = 0.08f + (1f - emotional) * 0.5f;
                    return HSVtoRGB(emotionalHue, 0.7f, intensity);
                default:
                    return Color.White;
            }
        }

        private void DrawBasicWaveform(
            Graphics g,
            float[] samples,
            ColorMode colorMode,
            BasicWaveformParameters parameters
        )
        {
            DebugWrite.Line(
                $"AudioVisualizerEngine.DrawBasicWaveform - Drawing with {samples.Length} samples."
            );
            if (samples.Length == 0)
            {
                DebugWrite.Line("AudioVisualizerEngine.DrawBasicWaveform - No samples to draw.");
                return;
            }

            var points = new List<PointF>();
            var step = Math.Max(1, samples.Length / _width); // Ensure step is at least 1
            float verticalCenter = _height * parameters.VerticalPosition;
            float waveHeight = _height * parameters.WaveHeight;

            for (int x = 0; x < _width; x++)
            {
                int index = x * step;
                if (index < samples.Length)
                {
                    float y = verticalCenter + samples[index] * waveHeight;
                    points.Add(new PointF(x, y));
                }
                else if (points.Count > 0)
                {
                    points.Add(new PointF(x, points.Last().Y));
                }
            }
            DebugWrite.Line(
                $"AudioVisualizerEngine.DrawBasicWaveform - Generated {points.Count} points for waveform. Step was {step}"
            );

            if (points.Count > 1)
            {
                // Draw glow effect first if enabled
                if (parameters.EnableGlow)
                {
                    for (int i = 0; i < points.Count - 1; i++)
                    {
                        int sampleIndex = i * step;
                        float intensity =
                            (sampleIndex < samples.Length)
                                ? Math.Abs(samples[sampleIndex]) * 2f
                                : 0.5f;
                        var color = GetColor(colorMode, intensity, (float)i / points.Count);
                        using (
                            var glowPen = new Pen(
                                Color.FromArgb(50, color),
                                parameters.LineThickness + 4
                            )
                        )
                        {
                            g.DrawLine(glowPen, points[i], points[i + 1]);
                        }
                    }
                }

                // Draw main waveform
                for (int i = 0; i < points.Count - 1; i++)
                {
                    int sampleIndex = i * step;
                    float intensity =
                        (sampleIndex < samples.Length) ? Math.Abs(samples[sampleIndex]) * 2f : 0.5f;
                    var color = GetColor(colorMode, intensity, (float)i / points.Count);
                    using (var pen = new Pen(color, parameters.LineThickness))
                    {
                        g.DrawLine(pen, points[i], points[i + 1]);
                    }
                }
                DebugWrite.Line(
                    $"AudioVisualizerEngine.DrawBasicWaveform - Drawn {points.Count - 1} line segments."
                );
            }
            else
            {
                DebugWrite.Line(
                    "AudioVisualizerEngine.DrawBasicWaveform - Not enough points to draw lines."
                );
            }
        }

        private void DrawCircularWave(
            Graphics g,
            float[] samples,
            ColorMode colorMode,
            CircularWaveParameters parameters
        )
        {
            DebugWrite.Line(
                $"AudioVisualizerEngine.DrawCircularWave - Drawing with {samples.Length} samples."
            );
            if (samples.Length == 0)
            {
                DebugWrite.Line("AudioVisualizerEngine.DrawCircularWave - No samples to draw.");
                return;
            }

            float centerX = _width * parameters.CenterX;
            float centerY = _height * parameters.CenterY;
            float baseRadius; // Will be determined based on image or parameters

            Image? centerImage = null; // Use nullable Image for proper disposal

            try
            {
                if (
                    !string.IsNullOrEmpty(parameters.CircleCenterFilePath)
                    && File.Exists(parameters.CircleCenterFilePath)
                )
                {
                    try
                    {
                        centerImage = Image.FromFile(parameters.CircleCenterFilePath);
                        DebugWrite.Line(
                            $"AudioVisualizerEngine.DrawCircularWave - Loaded center image: {parameters.CircleCenterFilePath}"
                        );

                        // Calculate position to draw the image centered
                        float imageX = centerX - centerImage.Width / 2.0f;
                        float imageY = centerY - centerImage.Height / 2.0f;

                        // Draw the image
                        g.DrawImage(
                            centerImage,
                            imageX,
                            imageY,
                            centerImage.Width,
                            centerImage.Height
                        );
                        DebugWrite.Line(
                            $"AudioVisualizerEngine.DrawCircularWave - Drew center image at ({imageX},{imageY}) with size ({centerImage.Width},{centerImage.Height})."
                        );

                        // Set baseRadius for the wave based on the image dimensions.
                        // The wave's base will align with a circle inscribed by the smaller dimension of the image.
                        baseRadius = Math.Min(centerImage.Width, centerImage.Height) / 2.0f;
                        DebugWrite.Line(
                            $"AudioVisualizerEngine.DrawCircularWave - BaseRadius set from image: {baseRadius}"
                        );
                    }
                    catch (Exception ex)
                    {
                        DebugWrite.Line(
                            $"AudioVisualizerEngine.DrawCircularWave - Error loading or drawing center image '{parameters.CircleCenterFilePath}': {ex.Message}"
                        );
                        // Safely dispose if partially loaded or error occurred after loading
                        centerImage?.Dispose();
                        centerImage = null; // Ensure it's null so it's not disposed again in finally if already handled

                        // Fallback to original baseRadius calculation if image loading/drawing fails
                        baseRadius = Math.Min(_width, _height) * parameters.BaseRadius;
                        DebugWrite.Line(
                            $"AudioVisualizerEngine.DrawCircularWave - Fallback BaseRadius due to image error: {baseRadius}"
                        );
                    }
                }
                else
                {
                    // No image path provided, or file doesn't exist. Use original baseRadius calculation.
                    baseRadius = Math.Min(_width, _height) * parameters.BaseRadius;
                    if (string.IsNullOrEmpty(parameters.CircleCenterFilePath))
                    {
                        DebugWrite.Line(
                            $"AudioVisualizerEngine.DrawCircularWave - No center image path provided. Using default BaseRadius: {baseRadius}"
                        );
                    }
                    else
                    {
                        DebugWrite.Line(
                            $"AudioVisualizerEngine.DrawCircularWave - Center image file not found: {parameters.CircleCenterFilePath}. Using default BaseRadius: {baseRadius}"
                        );
                    }
                }

                // Log the final center and baseRadius being used for the wave
                DebugWrite.Line(
                    $"AudioVisualizerEngine.DrawCircularWave - Effective - Center: ({centerX},{centerY}), BaseRadius: {baseRadius}"
                );

                var points = new List<PointF>();
                int sampleCount = Math.Min(samples.Length, parameters.SamplePoints);
                DebugWrite.Line(
                    $"AudioVisualizerEngine.DrawCircularWave - Will use {sampleCount} samples for the circle."
                );

                // Generate points for the main circle
                for (int i = 0; i < sampleCount; i++)
                {
                    float angle = (float)(i * 2 * Math.PI / sampleCount);
                    // Ensure currentSampleIndex is within bounds of the samples array
                    int currentSampleIndex =
                        (samples.Length == sampleCount || samples.Length == 0)
                            ? i % samples.Length
                            : (i * samples.Length / sampleCount) % samples.Length;
                    if (samples.Length == 0)
                        currentSampleIndex = 0; // Avoid division by zero if samples is empty somehow after initial check

                    float waveAmplitudeEffect =
                        samples.Length > 0 ? samples[currentSampleIndex] : 0f;
                    float radius =
                        baseRadius
                        + waveAmplitudeEffect * baseRadius * parameters.MaxRadiusMultiplier;

                    // Ensure radius is not negative
                    radius = Math.Max(0, radius);

                    float x = centerX + (float)Math.Cos(angle) * radius;
                    float y = centerY + (float)Math.Sin(angle) * radius;
                    points.Add(new PointF(x, y));
                    if (i < 5) // Log first few points for debugging
                        DebugWrite.Line(
                            $"AudioVisualizerEngine.DrawCircularWave - Point {i}: Angle={angle:F2}, SampleVal={waveAmplitudeEffect:F2}, Radius={radius:F2}, Pos=({x:F2},{y:F2})"
                        );
                }
                DebugWrite.Line(
                    $"AudioVisualizerEngine.DrawCircularWave - Generated {points.Count} points for circular wave."
                );

                if (points.Count > 1)
                {
                    // Draw main circle
                    for (int i = 0; i < points.Count; i++)
                    {
                        int next = (i + 1) % points.Count;
                        int currentSampleIndex =
                            (samples.Length == sampleCount || samples.Length == 0)
                                ? i % samples.Length
                                : (i * samples.Length / sampleCount) % samples.Length;
                        if (samples.Length == 0)
                            currentSampleIndex = 0;

                        float intensity =
                            samples.Length > 0 ? Math.Abs(samples[currentSampleIndex]) * 2f : 0f;
                        var color = GetColor(colorMode, intensity, (float)i / points.Count);
                        using (var pen = new Pen(color, parameters.LineThickness))
                        {
                            g.DrawLine(pen, points[i], points[next]);
                        }
                    }

                    // Draw multiple rings if enabled
                    if (parameters.DrawMultipleRings)
                    {
                        for (int ring = 1; ring < parameters.RingCount; ring++)
                        {
                            float ringScale = 1f - (ring * 0.2f); // Each ring is smaller
                            // Ensure ringScale is positive
                            if (ringScale <= 0)
                                continue;

                            float ringAlphaFactor = 1f - (ring * 0.3f); // Each ring is more transparent
                            ringAlphaFactor = Math.Max(0, Math.Min(1, ringAlphaFactor)); // Clamp alpha factor between 0 and 1

                            var ringPoints = new List<PointF>();
                            for (int i = 0; i < sampleCount; i++)
                            {
                                float angle = (float)(i * 2 * Math.PI / sampleCount);
                                int currentSampleIndex =
                                    (samples.Length == sampleCount || samples.Length == 0)
                                        ? i % samples.Length
                                        : (i * samples.Length / sampleCount) % samples.Length;
                                if (samples.Length == 0)
                                    currentSampleIndex = 0;

                                float waveAmplitudeEffect =
                                    samples.Length > 0 ? samples[currentSampleIndex] : 0f;
                                float radius =
                                    (baseRadius * ringScale) // Apply ringScale to the base radius of the ring
                                    + waveAmplitudeEffect
                                        * baseRadius // Amplitude effect should also be based on the original baseRadius...
                                        * parameters.MaxRadiusMultiplier
                                        * ringScale; // ...and then scaled by ringScale

                                // Ensure radius is not negative
                                radius = Math.Max(0, radius);

                                float x = centerX + (float)Math.Cos(angle) * radius;
                                float y = centerY + (float)Math.Sin(angle) * radius;
                                ringPoints.Add(new PointF(x, y));
                            }

                            if (ringPoints.Count > 1)
                            {
                                for (int i = 0; i < ringPoints.Count; i++)
                                {
                                    int next = (i + 1) % ringPoints.Count;
                                    int currentSampleIndex =
                                        (samples.Length == sampleCount || samples.Length == 0)
                                            ? i % samples.Length
                                            : (i * samples.Length / sampleCount) % samples.Length;
                                    if (samples.Length == 0)
                                        currentSampleIndex = 0;

                                    float intensity =
                                        samples.Length > 0
                                            ? Math.Abs(samples[currentSampleIndex]) * 2f
                                            : 0f;
                                    // Note: original code had 'intensity * ringAlpha' for the GetColor call,
                                    // but then used ringAlpha again for Color.FromArgb.
                                    // Applying alpha directly to the color from GetColor is usually better.
                                    var baseRingColor = GetColor(
                                        colorMode,
                                        intensity, // Intensity for color calculation
                                        (float)i / ringPoints.Count + ring * 0.1f // Offset color phase for different rings
                                    );

                                    Color finalRingColor = Color.FromArgb(
                                        (int)(ringAlphaFactor * baseRingColor.A), // Apply ringAlphaFactor to the alpha of the base color
                                        baseRingColor.R,
                                        baseRingColor.G,
                                        baseRingColor.B
                                    );

                                    using (
                                        var pen = new Pen(
                                            finalRingColor,
                                            parameters.LineThickness * ringScale // Scale line thickness for outer rings
                                        )
                                    )
                                    {
                                        g.DrawLine(pen, ringPoints[i], ringPoints[next]);
                                    }
                                }
                            }
                        }
                    }
                    DebugWrite.Line(
                        $"AudioVisualizerEngine.DrawCircularWave - Drawn {points.Count} line segments for the circle."
                    );
                }
                else
                {
                    DebugWrite.Line(
                        "AudioVisualizerEngine.DrawCircularWave - Not enough points to draw lines."
                    );
                }
            } // End of try block that starts before image loading
            finally
            {
                // Dispose the image if it was loaded and not already disposed due to an error
                centerImage?.Dispose();
            }
        }

        private void DrawSphericalPulse(
            Graphics g,
            float[] samples,
            ColorMode colorMode,
            SphericalPulseParameters parameters
        )
        {
            DebugWrite.Line(
                $"AudioVisualizerEngine.DrawSphericalPulse - Drawing with {samples.Length} samples."
            );
            if (samples.Length == 0 && _historyBuffer.Count == 0)
            {
                DebugWrite.Line(
                    "AudioVisualizerEngine.DrawSphericalPulse - No samples and no history to draw."
                );
                return;
            }

            float centerX = _width * parameters.CenterX;
            float centerY = _height * parameters.CenterY;

            int circleCount = Math.Min(_historyBuffer.Count, parameters.MaxCircles);
            var histories = _historyBuffer.ToArray();
            DebugWrite.Line(
                $"AudioVisualizerEngine.DrawSphericalPulse - Center: ({centerX},{centerY}), Drawing {circleCount} historical circles."
            );

            for (int h = 0; h < circleCount; h++)
            {
                float alpha = 1f - (float)h / circleCount * parameters.AlphaFalloff;
                var historySamples = histories[(histories.Length - 1) - h];
                if (historySamples == null || historySamples.Length == 0)
                {
                    DebugWrite.Line(
                        $"AudioVisualizerEngine.DrawSphericalPulse - History samples at index {h} is null or empty. Skipping."
                    );
                    continue;
                }

                float avgAmplitude = 0;
                int samplesToAverage = Math.Min(historySamples.Length, 100);
                if (samplesToAverage == 0)
                {
                    DebugWrite.Line(
                        $"AudioVisualizerEngine.DrawSphericalPulse - No history samples to average for circle {h}. Skipping."
                    );
                    continue;
                }

                for (int i = 0; i < samplesToAverage; i++)
                {
                    avgAmplitude += Math.Abs(historySamples[i]);
                }
                avgAmplitude /= samplesToAverage;

                float radius =
                    parameters.BaseRadius
                    + avgAmplitude * parameters.AmplitudeMultiplier
                    + h * parameters.RadiusGrowthRate;
                var color = GetColor(colorMode, alpha * 0.7f, avgAmplitude);
                DebugWrite.Line(
                    $"AudioVisualizerEngine.DrawSphericalPulse - Circle {h}: Alpha={alpha}, AvgAmplitude={avgAmplitude}, Radius={radius}, Color={color}"
                );

                using (
                    var pen = new Pen(
                        Color.FromArgb(Math.Max(0, Math.Min(255, (int)(alpha * 255))), color),
                        2
                    )
                )
                {
                    g.DrawEllipse(pen, centerX - radius, centerY - radius, radius * 2, radius * 2);
                }
            }
            DebugWrite.Line("AudioVisualizerEngine.DrawSphericalPulse - Finished drawing pulses.");
        }

        private void DrawSpectrumBars(
            Graphics g,
            ColorMode colorMode,
            SpectrumBarsParameters parameters
        )
        {
            DebugWrite.Line("AudioVisualizerEngine.DrawSpectrumBars - Drawing spectrum bars.");

            // --- Variables needed for frequency mapping ---
            // Assuming _sampleRate is a field in your AudioVisualizerEngine, initialized from Mp3FileReader.WaveFormat.SampleRate
            // If not, you'll need to pass it or use a default. For robust results, use the actual sample rate.
            float sampleRate = 44100; // DEFAULT - REPLACE WITH ACTUAL if available: this._sampleRate or similar
            // You can get it in LoadAudioData: this._sampleRate = reader.WaveFormat.SampleRate;
            // Or, if this function is in a different class, it might need to be passed in.
            // For now, putting a placeholder that you can update.

            int fftActualSize = _fftBuffer.Length / 2; // Number of useful magnitude bins
            if (fftActualSize <= 0 || parameters.BarCount <= 0)
            {
                DebugWrite.Line(
                    "AudioVisualizerEngine.DrawSpectrumBars - Invalid FFT buffer size or BarCount. Aborting."
                );
                return;
            }
            float hzPerBin = (sampleRate / 2.0f) / fftActualSize; // Frequency range per FFT bin

            float minDisplayFreq = 20.0f; // Minimum frequency to display
            float maxDisplayFreq = Math.Min(sampleRate / 2.0f, 20000.0f); // Max frequency to display (up to Nyquist or 20kHz)
            // --- End Variables ---


            float totalBarWidth = _width - (parameters.BarCount - 1) * parameters.BarSpacing;
            float barWidth = totalBarWidth / parameters.BarCount;

            DebugWrite.Line(
                $"AudioVisualizerEngine.DrawSpectrumBars - BarCount: {parameters.BarCount}, BarWidth: {barWidth}, FFT Bins: {fftActualSize}, Hz/Bin: {hzPerBin:F2}"
            );

            for (int i = 0; i < parameters.BarCount; i++)
            {
                float barMagnitude;

                if (parameters.LogarithmicScale)
                {
                    // Calculate the lower and upper frequency for this bar on a log scale
                    float bandLowFreq =
                        minDisplayFreq
                        * (float)
                            Math.Pow(
                                maxDisplayFreq / minDisplayFreq,
                                (double)i / parameters.BarCount
                            );
                    float bandHighFreq =
                        minDisplayFreq
                        * (float)
                            Math.Pow(
                                maxDisplayFreq / minDisplayFreq,
                                (double)(i + 1) / parameters.BarCount
                            );

                    // Convert frequencies to FFT bin indices
                    int binLow = (int)(bandLowFreq / hzPerBin);
                    int binHigh = (int)(bandHighFreq / hzPerBin);

                    // Ensure indices are within bounds and binHigh is at least binLow
                    binLow = Math.Max(0, Math.Min(binLow, fftActualSize - 1));
                    binHigh = Math.Max(binLow, Math.Min(binHigh, fftActualSize - 1));

                    // Take the maximum magnitude within this frequency band for the bar
                    float maxMagnitudeInBand = 0f;
                    // Iterate from binLow up to (but not necessarily including) binHigh,
                    // unless they are the same, in which case, take that one bin.
                    // The loop should be k <= binHigh if binHigh is inclusive upper bound of the band's bins.
                    // If binHigh is the start of the *next* band, then k < binHigh.
                    // Given bandHighFreq is (i+1)/BarCount, it's the end of the current band.
                    for (int k = binLow; k <= binHigh; k++)
                    {
                        if (k < fftActualSize) // Defensive check
                        {
                            if (_fftBuffer[k] > maxMagnitudeInBand)
                            {
                                maxMagnitudeInBand = _fftBuffer[k];
                            }
                        }
                    }
                    barMagnitude = maxMagnitudeInBand;

                    // Debug for first few and last few bars
                    if (i < 3 || i >= parameters.BarCount - 3)
                    {
                        DebugWrite.Line(
                            $"Log Bar {i}: FreqRange [{bandLowFreq:F1}-{bandHighFreq:F1} Hz] -> Bins [{binLow}-{binHigh}] -> Mag={barMagnitude:F4}"
                        );
                    }
                }
                else // Linear Scale
                {
                    // Each bar takes an equal slice of FFT bins
                    int startBin = (i * fftActualSize) / parameters.BarCount;
                    int endBin = ((i + 1) * fftActualSize) / parameters.BarCount - 1;

                    startBin = Math.Max(0, Math.Min(startBin, fftActualSize - 1));
                    endBin = Math.Max(startBin, Math.Min(endBin, fftActualSize - 1));

                    float maxMagnitudeInBand = 0f;
                    for (int k = startBin; k <= endBin; k++)
                    {
                        if (k < fftActualSize) // Defensive check
                        {
                            if (_fftBuffer[k] > maxMagnitudeInBand)
                            {
                                maxMagnitudeInBand = _fftBuffer[k];
                            }
                        }
                    }
                    barMagnitude = maxMagnitudeInBand;

                    // Debug for first few and last few bars
                    if (i < 3 || i >= parameters.BarCount - 3)
                    {
                        float bandLowFreq = startBin * hzPerBin;
                        float bandHighFreq = (endBin + 1) * hzPerBin;
                        DebugWrite.Line(
                            $"Lin Bar {i}: FreqRange [{bandLowFreq:F1}-{bandHighFreq:F1} Hz] -> Bins [{startBin}-{endBin}] -> Mag={barMagnitude:F4}"
                        );
                    }
                }

                float finalMagnitude = barMagnitude * parameters.AmplitudeMultiplier; // Apply global multiplier
                float barHeight = Math.Min(
                    finalMagnitude * _height * 0.75f, // Some arbitrary scaling based on screen height
                    _height * parameters.MaxBarHeight
                );
                barHeight = Math.Max(0, barHeight);

                float x = i * (barWidth + parameters.BarSpacing);
                float y = parameters.MirrorBars ? (_height - barHeight) / 2 : _height - barHeight;

                var color = GetColor(
                    colorMode,
                    Math.Min(finalMagnitude, 1f), // Intensity for color often capped at 1.0
                    (float)i / parameters.BarCount
                );

                using (var brush = new SolidBrush(color))
                {
                    g.FillRectangle(brush, x, y, barWidth, barHeight);
                    if (parameters.MirrorBars)
                    {
                        g.FillRectangle(brush, x, _height / 2, barWidth, barHeight);
                    }
                }

                if (parameters.EnableGlow)
                {
                    using (
                        var glowBrush = new SolidBrush(
                            Color.FromArgb((int)parameters.GlowIntensity, color)
                        )
                    )
                    {
                        g.FillRectangle(glowBrush, x - 2, y - 5, barWidth + 4, barHeight + 10);
                        if (parameters.MirrorBars)
                        {
                            g.FillRectangle(
                                glowBrush,
                                x - 2,
                                _height / 2 - 5,
                                barWidth + 4,
                                barHeight + 10
                            );
                        }
                    }
                }
            }
            DebugWrite.Line(
                "AudioVisualizerEngine.DrawSpectrumBars - Finished drawing spectrum bars."
            );
        }

        private void DrawCircularSpectrumBars(
            Graphics g,
            ColorMode colorMode,
            CircularSpectrumBarsParameters parameters
        )
        {
            DebugWrite.Line(
                "AudioVisualizerEngine.DrawCircularSpectrumBars - Drawing enhanced circular spectrum bars."
            );

            if (_fftBuffer == null || _fftBuffer.Length < 2)
            {
                DebugWrite.Line(
                    "AudioVisualizerEngine.DrawCircularSpectrumBars - FFT buffer is null or too small."
                );
                return;
            }

            // Enable high-quality rendering
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            float centerX = _width * parameters.CenterX;
            float centerY = _height * parameters.CenterY;
            float baseRadius;
            float imageRadius = 0;

            Image? centerImage = null;

            try
            {
                // 1. Image handling with better scaling and effects
                float imageX = 0,
                    imageY = 0,
                    imageWidth = 0,
                    imageHeight = 0;
                bool shouldDrawImage = false;

                if (
                    !string.IsNullOrEmpty(parameters.CircleCenterFilePath)
                    && File.Exists(parameters.CircleCenterFilePath)
                )
                {
                    try
                    {
                        centerImage = Image.FromFile(parameters.CircleCenterFilePath);

                        // Better image sizing calculation
                        float targetImageDiameter =
                            Math.Min(_width, _height) * parameters.BaseRadiusPercentage * 2.2f;
                        float aspectRatio = (float)centerImage.Width / centerImage.Height;

                        if (aspectRatio > 1)
                        {
                            imageWidth = targetImageDiameter;
                            imageHeight = targetImageDiameter / aspectRatio;
                        }
                        else
                        {
                            imageHeight = targetImageDiameter;
                            imageWidth = targetImageDiameter * aspectRatio;
                        }

                        imageX = centerX - imageWidth / 2.0f;
                        imageY = centerY - imageHeight / 2.0f;
                        shouldDrawImage = true;

                        imageRadius = Math.Min(imageWidth, imageHeight) / 2.0f;
                        baseRadius = imageRadius * 1.15f; // Better spacing ratio

                        DebugWrite.Line(
                            $"Enhanced image preparation - ImageRadius: {imageRadius}, BaseRadius: {baseRadius}"
                        );
                    }
                    catch (Exception ex)
                    {
                        DebugWrite.Line($"Error loading image: {ex.Message}");
                        centerImage?.Dispose();
                        centerImage = null;
                        baseRadius = Math.Min(_width, _height) * parameters.BaseRadiusPercentage;
                    }
                }
                else
                {
                    baseRadius = Math.Min(_width, _height) * parameters.BaseRadiusPercentage;
                }

                // 2. Enhanced FFT processing with better frequency distribution
                var processedMagnitudes = ProcessFFTData(parameters);

                // 3. Choose rendering mode based on UseContinuousWaves parameter
                if (parameters.UseContinuousWaves)
                {
                    DrawContinuousWaveSpectrum(
                        g,
                        colorMode,
                        parameters,
                        centerX,
                        centerY,
                        baseRadius,
                        imageRadius,
                        processedMagnitudes
                    );
                }
                else
                {
                    DrawSpectrumBarsWithEffects(
                        g,
                        colorMode,
                        parameters,
                        centerX,
                        centerY,
                        baseRadius,
                        imageRadius,
                        processedMagnitudes
                    );
                }

                // 4. Draw center image with enhanced effects
                if (shouldDrawImage && centerImage != null)
                {
                    DrawCenterImageWithEffects(
                        g,
                        centerImage,
                        imageX,
                        imageY,
                        imageWidth,
                        imageHeight,
                        parameters
                    );
                }

                // 5. Add optional particle effects around the spectrum
                if (parameters.EnableGlow) // Reuse this flag for particle effects
                {
                    DrawParticleEffects(
                        g,
                        colorMode,
                        centerX,
                        centerY,
                        baseRadius,
                        processedMagnitudes
                    );
                }
            }
            finally
            {
                centerImage?.Dispose();
                // Reset graphics quality for performance on subsequent draws
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;
            }

            DebugWrite.Line(
                "AudioVisualizerEngine.DrawCircularSpectrumBars - Enhanced rendering complete."
            );
        }

        private void DrawContinuousWaveSpectrum(
            Graphics g,
            ColorMode colorMode,
            CircularSpectrumBarsParameters parameters,
            float centerX,
            float centerY,
            float baseRadius,
            float imageRadius,
            float[] magnitudes
        )
        {
            DebugWrite.Line(
                "AudioVisualizerEngine.DrawContinuousWaveSpectrum - Drawing continuous wave spectrum."
            );

            // Create points for the continuous wave
            var outerPoints = new List<PointF>();
            var innerPoints = new List<PointF>();

            // Add rotation animation
            double rotationOffset = 0;

            // Generate smooth wave points
            for (int i = 0; i <= parameters.BarCount; i++) // <= to close the loop
            {
                int index = i % parameters.BarCount;
                float magnitude = magnitudes[index];

                // Smooth the magnitude with neighbors for continuous effect
                if (i > 0 && i < parameters.BarCount)
                {
                    float prevMag = magnitudes[
                        (index - 1 + parameters.BarCount) % parameters.BarCount
                    ];
                    float nextMag = magnitudes[(index + 1) % parameters.BarCount];
                    magnitude = (prevMag * 0.25f + magnitude * 0.5f + nextMag * 0.25f);
                }

                float barHeight = magnitude * baseRadius * parameters.BarHeightScaleFactor;
                barHeight = Math.Min(barHeight, baseRadius * parameters.MaxBarHeightRatio);
                barHeight = Math.Max(0, barHeight);

                double angle = (i * 2.0 * Math.PI / parameters.BarCount) + rotationOffset;

                // Calculate outer point (wave peak)
                float outerRadius = baseRadius + barHeight;
                outerPoints.Add(
                    new PointF(
                        centerX + (float)(Math.Cos(angle) * outerRadius),
                        centerY + (float)(Math.Sin(angle) * outerRadius)
                    )
                );

                // Calculate inner point (base of wave)
                float innerRadius = baseRadius;
                innerPoints.Add(
                    new PointF(
                        centerX + (float)(Math.Cos(angle) * innerRadius),
                        centerY + (float)(Math.Sin(angle) * innerRadius)
                    )
                );
            }

            // Create the wave path
            using (var wavePath = new System.Drawing.Drawing2D.GraphicsPath())
            {
                // Add outer curve (the wave)
                if (outerPoints.Count > 2)
                {
                    wavePath.AddCurve(outerPoints.ToArray(), 0.5f); // Tension for smoothness
                }

                // Close the path by connecting to inner circle
                if (innerPoints.Count > 2)
                {
                    innerPoints.Reverse();
                    wavePath.AddCurve(innerPoints.ToArray(), 0.5f);
                }

                wavePath.CloseFigure();

                // Create gradient brush for the fill
                using (
                    var gradientBrush = CreateRadialGradientBrush(
                        centerX,
                        centerY,
                        baseRadius,
                        outerPoints,
                        colorMode,
                        magnitudes
                    )
                )
                {
                    // Fill the wave with gradient
                    g.FillPath(gradientBrush, wavePath);
                }

                // Add glow effect if enabled
                if (parameters.EnableGlow)
                {
                    using (var glowPath = (System.Drawing.Drawing2D.GraphicsPath)wavePath.Clone())
                    {
                        using (
                            var glowBrush = new SolidBrush(
                                Color.FromArgb((int)parameters.GlowIntensity, Color.White)
                            )
                        )
                        {
                            // Scale up slightly for glow
                            var matrix = new System.Drawing.Drawing2D.Matrix();
                            matrix.Translate(-centerX, -centerY);
                            matrix.Scale(1.05f, 1.05f);
                            matrix.Translate(centerX, centerY);
                            glowPath.Transform(matrix);

                            g.FillPath(glowBrush, glowPath);
                        }
                    }
                }

                // Draw wave outline for definition
                var avgMagnitude = magnitudes.Average();
                var outlineColor = GetEnhancedColor(
                    colorMode,
                    avgMagnitude,
                    0.5f,
                    baseRadius * 0.5f,
                    baseRadius
                );
                using (var outlinePen = new Pen(Color.FromArgb(180, outlineColor), 2.0f))
                {
                    g.DrawPath(outlinePen, wavePath);
                }
            }

            // Draw mirrored inner wave if enabled
            if (parameters.MirrorBars && imageRadius > 0)
            {
                DrawContinuousInnerWave(
                    g,
                    colorMode,
                    parameters,
                    centerX,
                    centerY,
                    baseRadius,
                    imageRadius,
                    magnitudes
                );
            }
        }

        private void DrawContinuousInnerWave(
            Graphics g,
            ColorMode colorMode,
            CircularSpectrumBarsParameters parameters,
            float centerX,
            float centerY,
            float baseRadius,
            float imageRadius,
            float[] magnitudes
        )
        {
            var innerWavePoints = new List<PointF>();
            var innerBasePoints = new List<PointF>();

            double rotationOffset = _frameCount * 0.002;
            float innerWaveBase = Math.Max(imageRadius * 1.05f, baseRadius * 0.5f);

            for (int i = 0; i <= parameters.BarCount; i++)
            {
                int index = i % parameters.BarCount;
                float magnitude = magnitudes[index] * 0.7f; // Slightly reduced for inner wave

                // Smooth the magnitude
                if (i > 0 && i < parameters.BarCount)
                {
                    float prevMag =
                        magnitudes[(index - 1 + parameters.BarCount) % parameters.BarCount] * 0.7f;
                    float nextMag = magnitudes[(index + 1) % parameters.BarCount] * 0.7f;
                    magnitude = (prevMag * 0.25f + magnitude * 0.5f + nextMag * 0.25f);
                }

                float waveHeight =
                    magnitude * (baseRadius - innerWaveBase) * parameters.BarHeightScaleFactor;
                waveHeight = Math.Min(waveHeight, (baseRadius - innerWaveBase) * 0.8f);

                double angle = (i * 2.0 * Math.PI / parameters.BarCount) + rotationOffset;

                // Inner wave peak (grows inward)
                float innerRadius = innerWaveBase + waveHeight;
                innerWavePoints.Add(
                    new PointF(
                        centerX + (float)(Math.Cos(angle) * innerRadius),
                        centerY + (float)(Math.Sin(angle) * innerRadius)
                    )
                );

                // Inner wave base
                innerBasePoints.Add(
                    new PointF(
                        centerX + (float)(Math.Cos(angle) * innerWaveBase),
                        centerY + (float)(Math.Sin(angle) * innerWaveBase)
                    )
                );
            }

            using (var innerPath = new System.Drawing.Drawing2D.GraphicsPath())
            {
                if (innerWavePoints.Count > 2)
                {
                    innerPath.AddCurve(innerWavePoints.ToArray(), 0.5f);
                }

                if (innerBasePoints.Count > 2)
                {
                    innerBasePoints.Reverse();
                    innerPath.AddCurve(innerBasePoints.ToArray(), 0.5f);
                }

                innerPath.CloseFigure();

                var avgMagnitude = magnitudes.Average() * 0.7f;
                var innerColor = GetEnhancedColor(
                    colorMode,
                    avgMagnitude,
                    0.3f,
                    innerWaveBase * 0.3f,
                    baseRadius
                );

                using (var innerBrush = new SolidBrush(Color.FromArgb(150, innerColor)))
                {
                    g.FillPath(innerBrush, innerPath);
                }
            }
        }

        private Brush CreateRadialGradientBrush(
            float centerX,
            float centerY,
            float baseRadius,
            List<PointF> outerPoints,
            ColorMode colorMode,
            float[] magnitudes
        )
        {
            // Calculate the average outer radius for gradient
            float avgOuterRadius = 0;
            foreach (var point in outerPoints)
            {
                float dx = point.X - centerX;
                float dy = point.Y - centerY;
                avgOuterRadius += (float)Math.Sqrt(dx * dx + dy * dy);
            }
            avgOuterRadius /= outerPoints.Count;

            var gradientPath = new System.Drawing.Drawing2D.GraphicsPath();
            gradientPath.AddEllipse(
                centerX - avgOuterRadius,
                centerY - avgOuterRadius,
                avgOuterRadius * 2,
                avgOuterRadius * 2
            );

            var brush = new System.Drawing.Drawing2D.PathGradientBrush(gradientPath);

            // Set center point
            brush.CenterPoint = new PointF(centerX, centerY);

            // Create color blend based on frequency magnitudes
            var avgMagnitude = magnitudes.Average();
            var maxMagnitude = magnitudes.Max();

            var centerColor = GetEnhancedColor(
                colorMode,
                maxMagnitude,
                0.5f,
                avgOuterRadius - baseRadius,
                baseRadius
            );
            var edgeColor = GetEnhancedColor(
                colorMode,
                avgMagnitude * 0.5f,
                0.8f,
                (avgOuterRadius - baseRadius) * 0.5f,
                baseRadius
            );

            brush.CenterColor = Color.FromArgb(200, centerColor);
            brush.SurroundColors = new Color[] { Color.FromArgb(100, edgeColor) };

            // Add color blend for smooth gradient
            var blend = new System.Drawing.Drawing2D.ColorBlend(3);
            blend.Colors = new Color[]
            {
                Color.FromArgb(50, edgeColor),
                Color.FromArgb(150, centerColor),
                Color.FromArgb(200, centerColor),
            };
            blend.Positions = new float[] { 0.0f, 0.7f, 1.0f };
            brush.InterpolationColors = blend;

            return brush;
        }

        private Color GetDominantColor(List<Color> colors)
        {
            if (colors.Count == 0)
                return Color.White;

            // Calculate average color with proper weighting
            float totalR = 0,
                totalG = 0,
                totalB = 0;
            float totalWeight = 0;

            foreach (var color in colors)
            {
                // Weight by brightness to favor more vibrant colors
                float brightness = (color.R + color.G + color.B) / 765f;
                float weight = 0.5f + brightness * 0.5f;

                totalR += color.R * weight;
                totalG += color.G * weight;
                totalB += color.B * weight;
                totalWeight += weight;
            }

            if (totalWeight > 0)
            {
                return Color.FromArgb(
                    255,
                    Math.Min(255, (int)(totalR / totalWeight)),
                    Math.Min(255, (int)(totalG / totalWeight)),
                    Math.Min(255, (int)(totalB / totalWeight))
                );
            }

            return colors[colors.Count / 2]; // Fallback to middle color
        }

        private Color GetSecondaryColor(List<Color> colors, Color dominantColor)
        {
            if (colors.Count == 0)
                return dominantColor;

            // Find color most different from dominant
            Color mostDifferent = colors[0];
            float maxDifference = 0;

            foreach (var color in colors)
            {
                float diff =
                    Math.Abs(color.R - dominantColor.R)
                    + Math.Abs(color.G - dominantColor.G)
                    + Math.Abs(color.B - dominantColor.B);

                if (diff > maxDifference)
                {
                    maxDifference = diff;
                    mostDifferent = color;
                }
            }

            // Blend it slightly with dominant for harmony
            return Color.FromArgb(
                255,
                (mostDifferent.R + dominantColor.R) / 2,
                (mostDifferent.G + dominantColor.G) / 2,
                (mostDifferent.B + dominantColor.B) / 2
            );
        }

        // Add this field to the class where ProcessFFTData resides:
        private float[] _previousSmoothedMagnitudes;

        // MathHelper class assumed to be available (with Lerp)
        // VisualizationConstants class assumed to be available

        private float[] ProcessFFTData(CircularSpectrumBarsParameters parameters)
        {
            if (_fftBuffer == null || _fftBuffer.Length == 0 || parameters.BarCount <= 0)
            {
                // Return an array of zeros if FFT buffer is invalid or no bars to process
                if (
                    _previousSmoothedMagnitudes == null
                    || _previousSmoothedMagnitudes.Length != parameters.BarCount
                )
                {
                    _previousSmoothedMagnitudes = new float[
                        parameters.BarCount > 0 ? parameters.BarCount : 1
                    ];
                }
                Array.Clear(_previousSmoothedMagnitudes, 0, _previousSmoothedMagnitudes.Length);
                return _previousSmoothedMagnitudes;
            }

            // Initialize or resize _previousSmoothedMagnitudes if necessary
            if (
                _previousSmoothedMagnitudes == null
                || _previousSmoothedMagnitudes.Length != parameters.BarCount
            )
            {
                _previousSmoothedMagnitudes = new float[parameters.BarCount];
                // Optionally initialize with zeros or a small value
            }

            var currentMagnitudes = new float[parameters.BarCount];
            int _fftLength = _fftBuffer.Length / 2; // Usable part of the FFT spectrum (magnitudes)

            // --- Step 1: Map Bars to FFT Bins and Get Raw Magnitudes ---
            for (int i = 0; i < parameters.BarCount; i++)
            {
                int fftIndex;
                if (parameters.LogarithmicScale)
                {
                    if (_fftLength <= 1)
                    { // Handle edge case where _fftLength is too small
                        fftIndex = 0;
                    }
                    else
                    {
                        // Ensure logBase is > 1. If _fftLength is small, Pow can result in values close to 1.
                        double logBase = Math.Max(
                            1.000001,
                            Math.Pow(_fftLength, 1.0 / parameters.BarCount)
                        );
                        // (i + 1) in exponent to map bar 0 to logBase^1, bar_max to logBase^BarCount (~_fftLength)
                        // Subtract 1 to shift from [logBase^1, logBase^BarCount] range to [0, _fftLength-1] like indices
                        fftIndex = (int)(Math.Pow(logBase, i + 1.0) - logBase); // Adjusted for better distribution
                        // A common alternative is to define bands first, then map. This is a direct log mapping.
                    }
                }
                else
                {
                    fftIndex = (int)(((float)i / parameters.BarCount) * _fftLength);
                }

                fftIndex = Math.Max(0, Math.Min(fftIndex, _fftLength - 1)); // Clamp index
                currentMagnitudes[i] = _fftBuffer[fftIndex];
            }

            // --- Step 2: Apply Frequency-Dependent Amplification & Global Multiplier ---
            for (int i = 0; i < parameters.BarCount; i++)
            {
                float frequencyRatio = (float)i / (parameters.BarCount - 1); // Normalized position (0.0 to 1.0)
                // (BarCount - 1) ensures the last bar gets ratio 1.0
                if (parameters.BarCount <= 1)
                    frequencyRatio = 0.5f; // Avoid division by zero for single bar

                float amplification = 1.0f;

                if (frequencyRatio < BASS_BOOST_THRESHOLD)
                {
                    // Consistent boost across the defined bass band
                    amplification = BASS_AMPLIFICATION;
                }
                else if (frequencyRatio > TREBLE_BOOST_THRESHOLD)
                {
                    // Scale treble boost from 1.0 up to TREBLE_AMPLIFICATION across the treble band
                    float trebleRange = 1.0f - TREBLE_BOOST_THRESHOLD;
                    if (trebleRange <= 0)
                        trebleRange = 0.001f; // Avoid division by zero
                    float progressionInTreble =
                        (frequencyRatio - TREBLE_BOOST_THRESHOLD) / trebleRange;
                    amplification = 1.0f + (progressionInTreble * (TREBLE_AMPLIFICATION - 1.0f));
                }

                amplification = Math.Max(0.0f, amplification); // Ensure no negative amplification

                currentMagnitudes[i] *= parameters.AmplitudeMultiplier * amplification;
            }

            // --- Step 3: Apply Spatial Smoothing (across neighboring bars) ---
            var spatiallySmoothedMagnitudes = new float[parameters.BarCount];
            if (parameters.EnableSpatialSmoothing && parameters.BarCount > 0)
            {
                if (parameters.BarCount <= 2) // Handle cases with 1 or 2 bars (no full neighborhood)
                {
                    Array.Copy(currentMagnitudes, spatiallySmoothedMagnitudes, parameters.BarCount);
                }
                else // More than 2 bars, apply smoothing
                {
                    // Handle edges (can mirror, clamp, or use simpler smoothing)
                    spatiallySmoothedMagnitudes[0] =
                        currentMagnitudes[0] * parameters.SpatialSmoothFactorMid
                        + currentMagnitudes[1] * parameters.SpatialSmoothFactorHigh; // Simplified for edge
                    float sumFactorsEdge =
                        parameters.SpatialSmoothFactorMid + parameters.SpatialSmoothFactorHigh;
                    if (sumFactorsEdge > 0)
                        spatiallySmoothedMagnitudes[0] /= sumFactorsEdge; // Normalize if factors don't sum to 1

                    spatiallySmoothedMagnitudes[parameters.BarCount - 1] =
                        currentMagnitudes[parameters.BarCount - 1]
                            * parameters.SpatialSmoothFactorMid
                        + currentMagnitudes[parameters.BarCount - 2]
                            * parameters.SpatialSmoothFactorLow;
                    sumFactorsEdge =
                        parameters.SpatialSmoothFactorMid + parameters.SpatialSmoothFactorLow;
                    if (sumFactorsEdge > 0)
                        spatiallySmoothedMagnitudes[parameters.BarCount - 1] /= sumFactorsEdge;

                    for (int i = 1; i < parameters.BarCount - 1; i++)
                    {
                        spatiallySmoothedMagnitudes[i] =
                            currentMagnitudes[i - 1] * parameters.SpatialSmoothFactorLow
                            + currentMagnitudes[i] * parameters.SpatialSmoothFactorMid
                            + currentMagnitudes[i + 1] * parameters.SpatialSmoothFactorHigh;
                        // Optional: if factors don't sum to 1, normalize:
                        // float sumFactors = parameters.SpatialSmoothFactorLow + parameters.SpatialSmoothFactorMid + parameters.SpatialSmoothFactorHigh;
                        // if (sumFactors > 0) spatiallySmoothedMagnitudes[i] /= sumFactors;
                    }
                }
            }
            else
            {
                Array.Copy(currentMagnitudes, spatiallySmoothedMagnitudes, parameters.BarCount);
            }

            // --- Step 4: Apply Temporal Smoothing (across frames) ---
            if (parameters.EnableTemporalSmoothing)
            {
                for (int i = 0; i < parameters.BarCount; i++)
                {
                    // Use MathHelper.Lerp if available, otherwise standard lerp:
                    // _previousSmoothedMagnitudes[i] = firstFloat * (1 - by) + secondFloat * by;
                    _previousSmoothedMagnitudes[i] =
                        _previousSmoothedMagnitudes[i] * (1 - parameters.TemporalSmoothFactor)
                        + spatiallySmoothedMagnitudes[i] * parameters.TemporalSmoothFactor;
                }
            }
            else
            {
                Array.Copy(
                    spatiallySmoothedMagnitudes,
                    _previousSmoothedMagnitudes,
                    parameters.BarCount
                );
            }

            return _previousSmoothedMagnitudes;
        }

        private void DrawSpectrumBarsWithEffects(
            Graphics g,
            ColorMode colorMode,
            CircularSpectrumBarsParameters parameters,
            float centerX,
            float centerY,
            float baseRadius,
            float imageRadius,
            float[] magnitudes
        )
        {
            double anglePerBar = (2.0 * Math.PI) / parameters.BarCount;
            double barAngularWidth = anglePerBar * parameters.BarFillRatio;
            double halfBarWidth = barAngularWidth / 2.0;

            // Create advanced gradient brushes for better visual appeal
            var gradientCenter = new PointF(centerX, centerY);

            for (int i = 0; i < parameters.BarCount; i++)
            {
                float magnitude = magnitudes[i];
                float barHeight = magnitude * baseRadius * parameters.BarHeightScaleFactor;
                barHeight = Math.Min(barHeight, baseRadius * parameters.MaxBarHeightRatio);
                barHeight = Math.Max(0, barHeight);

                if (barHeight < 0.5f)
                    continue; // Skip tiny bars

                // Enhanced angle calculations with rotation animation
                double rotationOffset = _frameCount * 0.002; // Slow rotation
                double barCenterAngle = i * anglePerBar + rotationOffset;
                double barStartAngle = barCenterAngle - halfBarWidth;
                double barEndAngle = barCenterAngle + halfBarWidth;

                // Calculate bar geometry with sub-pixel precision
                var barGeometry = CalculateBarGeometry(
                    centerX,
                    centerY,
                    baseRadius,
                    barHeight,
                    barStartAngle,
                    barEndAngle
                );

                // Enhanced color calculation with more dynamic range
                var barColor = GetEnhancedColor(
                    colorMode,
                    magnitude,
                    (float)i / parameters.BarCount,
                    barHeight,
                    baseRadius
                );

                // Draw multiple layers for depth effect
                DrawBarWithLayers(g, barGeometry, barColor, parameters, magnitude);

                // Draw mirrored bars with enhanced effects
                if (parameters.MirrorBars && baseRadius > barHeight * 1.5f)
                {
                    DrawMirroredBar(
                        g,
                        centerX,
                        centerY,
                        baseRadius,
                        imageRadius,
                        barHeight,
                        barStartAngle,
                        barEndAngle,
                        barColor,
                        parameters,
                        magnitude
                    );
                }

                // Add reactive pulse effects for high-energy bars
                if (magnitude > 0.7f && parameters.EnableGlow)
                {
                    DrawPulseEffect(
                        g,
                        centerX,
                        centerY,
                        barCenterAngle,
                        baseRadius + barHeight,
                        barColor,
                        magnitude
                    );
                }
            }
        }

        private BarGeometry CalculateBarGeometry(
            float centerX,
            float centerY,
            float baseRadius,
            float barHeight,
            double startAngle,
            double endAngle
        )
        {
            // Calculate points with sub-pixel precision for smoother rendering
            var geometry = new BarGeometry();

            float outerRadius = baseRadius + barHeight;

            // Use more points for smoother curves on larger bars
            int curvePoints = Math.Max(3, (int)(barHeight / 10)); // More points for taller bars

            geometry.InnerPoints = new PointF[curvePoints];
            geometry.OuterPoints = new PointF[curvePoints];

            for (int i = 0; i < curvePoints; i++)
            {
                double angle = startAngle + (endAngle - startAngle) * i / (curvePoints - 1);

                geometry.InnerPoints[i] = new PointF(
                    centerX + (float)(Math.Cos(angle) * baseRadius),
                    centerY + (float)(Math.Sin(angle) * baseRadius)
                );

                geometry.OuterPoints[i] = new PointF(
                    centerX + (float)(Math.Cos(angle) * outerRadius),
                    centerY + (float)(Math.Sin(angle) * outerRadius)
                );
            }

            return geometry;
        }

        private Color GetEnhancedColor(
            ColorMode colorMode,
            float magnitude,
            float position,
            float barHeight,
            float baseRadius
        )
        {
            // Enhanced color calculation with more vibrant and dynamic colors
            float intensity = Math.Min(1.0f, magnitude * 2.0f);
            float heightRatio = barHeight / (baseRadius * 0.8f); // Normalize height

            Color baseColor = GetColor(colorMode, intensity, position);

            // Add height-based color modification for more dynamic appearance
            if (heightRatio > 0.5f)
            {
                // Brighten tall bars
                float brightenFactor = Math.Min(1.5f, 1.0f + heightRatio);
                int r = Math.Min(255, (int)(baseColor.R * brightenFactor));
                int gr = Math.Min(255, (int)(baseColor.G * brightenFactor));
                int b = Math.Min(255, (int)(baseColor.B * brightenFactor));
                baseColor = Color.FromArgb(baseColor.A, r, gr, b);
            }

            return baseColor;
        }

        private void DrawBarWithLayers(
            Graphics g,
            BarGeometry geometry,
            Color baseColor,
            CircularSpectrumBarsParameters parameters,
            float magnitude
        )
        {
            // Create polygon from geometry points
            var allPoints = new List<PointF>();
            allPoints.AddRange(geometry.InnerPoints);
            geometry.OuterPoints.Reverse();
            var geom = geometry.OuterPoints;
            geom.Reverse();
            allPoints.AddRange(geom);

            var polygon = allPoints.ToArray();

            // Layer 1: Glow effect (if enabled)
            if (parameters.EnableGlow && magnitude > 0.3f)
            {
                using (
                    var glowBrush = new SolidBrush(
                        Color.FromArgb(
                            Math.Min(255, parameters.GlowIntensity * 2),
                            baseColor.R,
                            baseColor.G,
                            baseColor.B
                        )
                    )
                )
                {
                    // Expand polygon slightly for glow
                    var glowPolygon = ExpandPolygon(polygon, parameters.GlowOffset);
                    g.FillPolygon(glowBrush, glowPolygon);
                }
            }

            // Layer 2: Main bar with gradient
            using (var mainBrush = CreateGradientBrush(geometry, baseColor, magnitude))
            {
                g.FillPolygon(mainBrush, polygon);
            }

            // Layer 3: Highlight edge for 3D effect
            if (magnitude > 0.5f)
            {
                using (var highlightPen = new Pen(Color.FromArgb(100, Color.White), 1.0f))
                {
                    // Draw highlight on the outer edge
                    g.DrawLines(highlightPen, geometry.OuterPoints);
                }
            }
        }

        private Brush CreateGradientBrush(BarGeometry geometry, Color baseColor, float magnitude)
        {
            if (geometry.InnerPoints.Length < 2 || geometry.OuterPoints.Length < 2)
                return new SolidBrush(baseColor);

            // Create radial gradient from inner to outer edge
            var innerCenter = GetCenterPoint(geometry.InnerPoints);
            var outerCenter = GetCenterPoint(geometry.OuterPoints);

            // Create gradient that goes from darker inner to brighter outer
            var darkerColor = Color.FromArgb(
                baseColor.A,
                Math.Max(0, baseColor.R - 40),
                Math.Max(0, baseColor.G - 40),
                Math.Max(0, baseColor.B - 40)
            );

            try
            {
                return new System.Drawing.Drawing2D.LinearGradientBrush(
                    innerCenter,
                    outerCenter,
                    darkerColor,
                    baseColor
                );
            }
            catch
            {
                return new SolidBrush(baseColor);
            }
        }

        private void DrawMirroredBar(
            Graphics g,
            float centerX,
            float centerY,
            float baseRadius,
            float imageRadius,
            float barHeight,
            double startAngle,
            double endAngle,
            Color barColor,
            CircularSpectrumBarsParameters parameters,
            float magnitude
        )
        {
            float mirrorInnerRadius = Math.Max(imageRadius * 1.05f, baseRadius - barHeight);

            var mirrorGeometry = CalculateBarGeometry(
                centerX,
                centerY,
                mirrorInnerRadius,
                baseRadius - mirrorInnerRadius,
                startAngle,
                endAngle
            );

            // Slightly transparent for layered effect
            var mirrorColor = Color.FromArgb(
                Math.Max(50, barColor.A - 100),
                barColor.R,
                barColor.G,
                barColor.B
            );

            DrawBarWithLayers(g, mirrorGeometry, mirrorColor, parameters, magnitude * 0.7f);
        }

        private void DrawCenterImageWithEffects(
            Graphics g,
            Image centerImage,
            float imageX,
            float imageY,
            float imageWidth,
            float imageHeight,
            CircularSpectrumBarsParameters parameters
        )
        {
            // Add subtle drop shadow
            using (var shadowBrush = new SolidBrush(Color.FromArgb(50, 0, 0, 0)))
            {
                g.FillEllipse(shadowBrush, imageX + 3, imageY + 3, imageWidth, imageHeight);
            }

            // Create circular clipping path for the image
            using (var clipPath = new System.Drawing.Drawing2D.GraphicsPath())
            {
                clipPath.AddEllipse(imageX, imageY, imageWidth, imageHeight);
                var oldClip = g.Clip;
                g.SetClip(clipPath);

                // Draw the image with high quality scaling
                g.DrawImage(centerImage, imageX, imageY, imageWidth, imageHeight);

                g.Clip = oldClip;
            }

            // Add subtle outer glow to the image
            using (var glowPen = new Pen(Color.FromArgb(30, Color.White), 3.0f))
            {
                g.DrawEllipse(glowPen, imageX - 1, imageY - 1, imageWidth + 2, imageHeight + 2);
            }
        }

        private void DrawParticleEffects(
            Graphics g,
            ColorMode colorMode,
            float centerX,
            float centerY,
            float baseRadius,
            float[] magnitudes
        )
        {
            // Add floating particles that react to the music
            var random = new Random(_frameCount); // Deterministic but animated

            for (int i = 0; i < Math.Min(20, magnitudes.Length / 3); i++)
            {
                if (magnitudes[i * 3] < 0.4f)
                    continue; // Only show particles for active frequencies

                float angle = (float)(random.NextDouble() * Math.PI * 2);
                float distance = baseRadius + magnitudes[i * 3] * baseRadius * 0.5f + 20;

                float particleX = centerX + (float)Math.Cos(angle) * distance;
                float particleY = centerY + (float)Math.Sin(angle) * distance;

                var particleColor = GetColor(
                    colorMode,
                    magnitudes[i * 3],
                    (float)i / magnitudes.Length
                );
                var alpha = Math.Max(50, Math.Min(200, (int)(magnitudes[i * 3] * 255)));

                using (var particleBrush = new SolidBrush(Color.FromArgb(alpha, particleColor)))
                {
                    float size = 2f + magnitudes[i * 3] * 4f;
                    g.FillEllipse(
                        particleBrush,
                        particleX - size / 2,
                        particleY - size / 2,
                        size,
                        size
                    );
                }
            }
        }

        private void DrawPulseEffect(
            Graphics g,
            float centerX,
            float centerY,
            double angle,
            float radius,
            Color color,
            float magnitude
        )
        {
            // Draw expanding pulse rings for high-energy bars
            float pulseX = centerX + (float)Math.Cos(angle) * radius;
            float pulseY = centerY + (float)Math.Sin(angle) * radius;

            for (int ring = 0; ring < 3; ring++)
            {
                float ringRadius = 5f + ring * 8f + magnitude * 10f;
                int alpha = Math.Max(10, 100 - ring * 30);

                using (var pulsePen = new Pen(Color.FromArgb(alpha, color), 2f - ring * 0.5f))
                {
                    g.DrawEllipse(
                        pulsePen,
                        pulseX - ringRadius,
                        pulseY - ringRadius,
                        ringRadius * 2,
                        ringRadius * 2
                    );
                }
            }
        }

        // Helper methods
        private PointF[] ExpandPolygon(PointF[] polygon, float expansion)
        {
            var center = GetCenterPoint(polygon);
            return polygon
                .Select(p => new PointF(
                    center.X + (p.X - center.X) * (1 + expansion / 100f),
                    center.Y + (p.Y - center.Y) * (1 + expansion / 100f)
                ))
                .ToArray();
        }

        private PointF GetCenterPoint(PointF[] points)
        {
            float avgX = points.Average(p => p.X);
            float avgY = points.Average(p => p.Y);
            return new PointF(avgX, avgY);
        }

        // Helper class for bar geometry
        private class BarGeometry
        {
            public PointF[] InnerPoints { get; set; }
            public PointF[] OuterPoints { get; set; }
        }

        private void DrawParticleFlow(
            Graphics g,
            float[] samples,
            ColorMode colorMode,
            ParticleFlowParameters parameters
        )
        {
            DebugWrite.Line(
                $"AudioVisualizerEngine.DrawParticleFlow - Drawing with {samples.Length} samples. Particle count: {_particles.Count}"
            );
            if (samples.Length == 0)
            {
                DebugWrite.Line(
                    "AudioVisualizerEngine.DrawParticleFlow - No samples for particle flow logic this frame."
                );
            }

            float avgAmplitude = 0;
            if (samples.Length > 0)
            {
                avgAmplitude = samples
                    .Take(Math.Min(samples.Length, 100))
                    .Select(Math.Abs)
                    .Average();
            }
            DebugWrite.Line(
                $"AudioVisualizerEngine.DrawParticleFlow - AvgAmplitude: {avgAmplitude}"
            );

            // Spawn new particles based on parameters
            if (
                avgAmplitude > parameters.SpawnThreshold
                && _particles.Count < parameters.MaxParticles
            )
            {
                int particlesToSpawn = (int)(avgAmplitude * parameters.SpawnRate);
                particlesToSpawn = Math.Min(particlesToSpawn, parameters.SpawnRate);
                DebugWrite.Line(
                    $"AudioVisualizerEngine.DrawParticleFlow - Spawning {particlesToSpawn} new particles."
                );
                for (int i = 0; i < particlesToSpawn; i++)
                {
                    var newParticle = new Particle
                    {
                        X = _random.Next(_width),
                        Y = _random.Next(_height),
                        VX =
                            (_random.NextSingle() - 0.5f)
                            * avgAmplitude
                            * parameters.VelocityMultiplier,
                        VY =
                            (_random.NextSingle() - 0.5f)
                            * avgAmplitude
                            * parameters.VelocityMultiplier,
                        Life = 1f,
                        Size =
                            parameters.BaseParticleSize
                            + _random.NextSingle()
                                * (parameters.MaxParticleSize - parameters.BaseParticleSize),
                    };
                    _particles.Add(newParticle);
                    if (i == 0)
                        DebugWrite.Line(
                            $"AudioVisualizerEngine.DrawParticleFlow - New particle: X={newParticle.X}, Y={newParticle.Y}, VX={newParticle.VX}, VY={newParticle.VY}"
                        );
                }
            }

            DebugWrite.Line(
                $"AudioVisualizerEngine.DrawParticleFlow - Updating and drawing {_particles.Count} particles."
            );
            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                var p = _particles[i];

                p.X += p.VX;
                p.Y += p.VY;
                p.Life -= parameters.LifeDecayRate;

                // Dampen velocity
                p.VX *= parameters.DampingFactor;
                p.VY *= parameters.DampingFactor;

                // Apply audio-reactive forces if enabled
                if (parameters.EnableAudioForces)
                {
                    p.VX += (_smoothBass - 0.3f) * parameters.ForceMultiplier * Math.Sign(p.VX);
                    p.VY += (_smoothMid - 0.3f) * parameters.ForceMultiplier * Math.Sign(p.VY);
                }

                if (
                    p.Life <= 0
                    || p.X < -p.Size
                    || p.X > _width + p.Size
                    || p.Y < -p.Size
                    || p.Y > _height + p.Size
                )
                {
                    _particles.RemoveAt(i);
                    continue;
                }

                var color = GetColor(colorMode, p.Life, p.X / _width);
                using (
                    var brush = new SolidBrush(
                        Color.FromArgb(Math.Max(0, Math.Min(255, (int)(p.Life * 255))), color)
                    )
                )
                {
                    g.FillEllipse(brush, p.X - p.Size / 2, p.Y - p.Size / 2, p.Size, p.Size);
                }
            }
            DebugWrite.Line(
                "AudioVisualizerEngine.DrawParticleFlow - Finished particle flow frame."
            );
        }

        private void DrawKaleidoscopeWave(
            Graphics g,
            float[] samples,
            ColorMode colorMode,
            KaleidoscopeWaveParameters parameters
        )
        {
            DebugWrite.Line(
                $"AudioVisualizerEngine.DrawKaleidoscopeWave - Drawing with {samples.Length} samples."
            );
            if (samples.Length == 0)
            {
                DebugWrite.Line("AudioVisualizerEngine.DrawKaleidoscopeWave - No samples to draw.");
                return;
            }

            float centerX = _width * parameters.CenterX;
            float centerY = _height * parameters.CenterY;
            DebugWrite.Line(
                $"AudioVisualizerEngine.DrawKaleidoscopeWave - Center: ({centerX},{centerY}), Segments: {parameters.Segments}"
            );

            g.TranslateTransform(centerX, centerY);
            DebugWrite.Line("AudioVisualizerEngine.DrawKaleidoscopeWave - Translated to center.");

            for (int seg = 0; seg < parameters.Segments; seg++)
            {
                g.RotateTransform(360f / parameters.Segments);

                var points = new List<PointF>();
                int sampleStep = Math.Max(1, samples.Length / parameters.PointsPerSegment);

                for (int i = 0; i < parameters.PointsPerSegment; i++)
                {
                    int index = i * sampleStep;
                    if (index < samples.Length)
                    {
                        float r =
                            parameters.BaseRadius
                            + i * parameters.RadiusGrowthRate
                            + samples[index] * parameters.WaveAmplitude;
                        float angle = i * parameters.SpiralTightness;
                        float x = r * (float)Math.Cos(angle);
                        float y = r * (float)Math.Sin(angle);
                        points.Add(new PointF(x, y));
                    }
                    else if (points.Any())
                    {
                        points.Add(points.Last());
                    }
                }
                DebugWrite.Line(
                    $"AudioVisualizerEngine.DrawKaleidoscopeWave - Segment {seg}: Generated {points.Count} points. SampleStep: {sampleStep}"
                );

                if (points.Count > 1)
                {
                    for (int i = 0; i < points.Count - 1; i++)
                    {
                        int index = i * sampleStep;
                        float intensity =
                            (index < samples.Length) ? Math.Abs(samples[index]) * 2f : 0.5f;
                        var color = GetColor(
                            colorMode,
                            intensity * 0.7f,
                            (float)i / points.Count + (float)seg / parameters.Segments
                        );
                        using (var pen = new Pen(color, parameters.LineThickness))
                        {
                            g.DrawLine(pen, points[i], points[i + 1]);
                        }
                    }
                }
            }

            g.ResetTransform();
            DebugWrite.Line(
                "AudioVisualizerEngine.DrawKaleidoscopeWave - Transform reset. Drawing complete."
            );
        }

        private void DrawDNAHelix(
            Graphics g,
            float[] samples,
            ColorMode colorMode,
            DNAHelixParameters parameters
        )
        {
            DebugWrite.Line(
                $"AudioVisualizerEngine.DrawDNAHelix - Drawing with {samples.Length} samples."
            );
            if (samples.Length == 0)
            {
                DebugWrite.Line("AudioVisualizerEngine.DrawDNAHelix - No samples to draw.");
                return;
            }

            float centerX = _width * parameters.CenterX;
            int numPointsInHelix = Math.Min(samples.Length, parameters.HelixPoints);
            float yStep = (float)_height / numPointsInHelix;
            DebugWrite.Line(
                $"AudioVisualizerEngine.DrawDNAHelix - CenterX: {centerX}, Points in Helix: {numPointsInHelix}, Y-Step: {yStep}"
            );

            for (int i = 0; i < numPointsInHelix; i++)
            {
                float y = i * yStep;
                float phase = y * parameters.WaveFrequency + _frameCount * parameters.HelixSpeed;

                int sampleIndex = i * samples.Length / numPointsInHelix;

                float radiusModifier =
                    parameters.HelixRadius
                    + samples[sampleIndex] * (_width * parameters.RadiusAmplitudeMultiplier);
                float x1 = centerX + (float)Math.Sin(phase) * radiusModifier;
                float x2 = centerX + (float)Math.Sin(phase + Math.PI) * radiusModifier;

                float intensity = Math.Abs(samples[sampleIndex]) * 2f;
                var color1 = GetColor(colorMode, intensity, phase % 1f);
                var color2 = GetColor(colorMode, intensity, (phase + 0.5f) % 1f);

                if (i < 2)
                    DebugWrite.Line(
                        $"AudioVisualizerEngine.DrawDNAHelix - Point {i}: Y={y}, Phase={phase}, SampleVal={samples[sampleIndex]}, X1={x1}, X2={x2}"
                    );

                using (var brush1 = new SolidBrush(color1))
                using (var brush2 = new SolidBrush(color2))
                {
                    float halfNodeSize = parameters.NodeSize / 2;
                    g.FillEllipse(
                        brush1,
                        x1 - halfNodeSize,
                        y - halfNodeSize,
                        parameters.NodeSize,
                        parameters.NodeSize
                    );
                    g.FillEllipse(
                        brush2,
                        x2 - halfNodeSize,
                        y - halfNodeSize,
                        parameters.NodeSize,
                        parameters.NodeSize
                    );
                }

                if (parameters.DrawConnections && i % parameters.ConnectionFrequency == 0)
                {
                    using (
                        var pen = new Pen(
                            Color.FromArgb(
                                100,
                                GetColor(colorMode, intensity * 0.5f, (phase + 0.25f) % 1f)
                            ),
                            2
                        )
                    )
                    {
                        g.DrawLine(pen, x1, y, x2, y);
                    }
                }
            }
            DebugWrite.Line("AudioVisualizerEngine.DrawDNAHelix - Finished drawing helix.");
        }

        private void DrawAurora(
            Graphics g,
            float[] samples,
            ColorMode colorMode,
            AuroraParameters parameters
        )
        {
            DebugWrite.Line(
                $"AudioVisualizerEngine.DrawAurora - Drawing with {samples.Length} samples."
            );
            if (samples.Length == 0)
            {
                DebugWrite.Line("AudioVisualizerEngine.DrawAurora - No samples to draw.");
                return;
            }

            int bandCount =
                parameters.MinBands
                + (int)(_smoothMid * (parameters.MaxBands - parameters.MinBands));
            bandCount = Math.Max(parameters.MinBands, Math.Min(bandCount, parameters.MaxBands));
            DebugWrite.Line($"AudioVisualizerEngine.DrawAurora - BandCount: {bandCount}");

            for (int band = 0; band < bandCount; band++)
            {
                var points = new List<PointF>();
                int pointStep = 10;

                for (int x = 0; x <= _width; x += pointStep)
                {
                    float baseY =
                        _height * (0.2f + (float)band / bandCount * parameters.BandSpread);
                    float wave1 =
                        (float)
                            Math.Sin(
                                x * (0.005f + band * 0.001f)
                                    + _frameCount
                                        * (
                                            parameters.WaveSpeed
                                            + _smoothBass * parameters.WaveSpeed
                                        )
                                    + band
                            ) * (_height * parameters.WaveAmplitude);
                    float wave2 =
                        (float)
                            Math.Sin(
                                x * (0.003f - band * 0.0005f)
                                    - _frameCount
                                        * (
                                            parameters.WaveSpeed * 0.5f
                                            + _smoothHigh * parameters.WaveSpeed
                                        )
                                    + band * 0.5f
                            ) * (_height * parameters.WaveAmplitude * 0.5f);

                    int sampleIndex = Math.Min(samples.Length - 1, (x * samples.Length) / _width);
                    if (sampleIndex < 0)
                        sampleIndex = 0;

                    float audioInfluence =
                        samples[sampleIndex] * (_height * parameters.AudioAmplitude);
                    float y = baseY + wave1 + wave2 + audioInfluence;
                    y = Math.Max(0, Math.Min(_height, y));
                    points.Add(new PointF(x, y));
                }
                if (band == 0 && points.Count > 0)
                    DebugWrite.Line(
                        $"AudioVisualizerEngine.DrawAurora - Band {band}: Generated {points.Count} points. First point: {points.First()}, Last point: {points.Last()}"
                    );

                if (points.Count > 2)
                {
                    using (var path = new System.Drawing.Drawing2D.GraphicsPath())
                    {
                        path.AddCurve(points.ToArray());
                        DebugWrite.Line(
                            $"AudioVisualizerEngine.DrawAurora - Band {band}: Curve added to path with {points.Count} points."
                        );

                        if (points.Any())
                        {
                            path.AddLine(points.Last(), new PointF(_width, _height));
                            path.AddLine(new PointF(_width, _height), new PointF(0, _height));
                            path.AddLine(new PointF(0, _height), points.First());
                            DebugWrite.Line(
                                $"AudioVisualizerEngine.DrawAurora - Band {band}: Path closed for filling."
                            );
                        }

                        float intensity =
                            parameters.IntensityBase
                            + (_smoothMid + _smoothBass) * parameters.IntensityMultiplier;
                        intensity = Math.Min(1f, Math.Max(0.1f, intensity));

                        var baseColor = GetColor(
                            colorMode,
                            intensity,
                            (float)band / bandCount + _frameCount * 0.002f
                        );

                        if (parameters.EnableGradient)
                        {
                            Color topColor = Color.FromArgb(
                                Math.Min(255, Math.Max(0, (int)(intensity * 150))),
                                baseColor
                            );
                            Color bottomColor = Color.FromArgb(0, baseColor);

                            if (points.First().Y < _height && points.Last().Y < _height)
                            {
                                using (
                                    var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                                        new PointF(0, points.Min(p => p.Y)),
                                        new PointF(0, _height),
                                        topColor,
                                        bottomColor
                                    )
                                )
                                {
                                    g.FillPath(brush, path);
                                    DebugWrite.Line(
                                        $"AudioVisualizerEngine.DrawAurora - Band {band}: Path filled with gradient. Intensity: {intensity}, TopColor: {topColor}"
                                    );
                                }
                            }
                        }
                        else
                        {
                            // Solid fill without gradient
                            using (
                                var brush = new SolidBrush(
                                    Color.FromArgb((int)(intensity * 100), baseColor)
                                )
                            )
                            {
                                g.FillPath(brush, path);
                            }
                        }
                    }
                }
                else
                {
                    DebugWrite.Line(
                        $"AudioVisualizerEngine.DrawAurora - Band {band}: Not enough points to draw curve ({points.Count})."
                    );
                }
            }
            DebugWrite.Line("AudioVisualizerEngine.DrawAurora - Finished drawing aurora bands.");
        }

        private void InitializeParticles(int count)
        {
            DebugWrite.Line(
                $"AudioVisualizerEngine.InitializeParticles - Initializing {count} particles."
            );
            _particles.Clear();
            for (int i = 0; i < count; i++)
            {
                var p = new Particle
                {
                    X = _random.Next(_width),
                    Y = _random.Next(_height),
                    VX = (_random.NextSingle() - 0.5f) * 2,
                    VY = (_random.NextSingle() - 0.5f) * 2,
                    Life = _random.NextSingle(),
                    Size = _random.NextSingle() * 5 + 1,
                };
                _particles.Add(p);
                if (i < 3)
                    DebugWrite.Line(
                        $"AudioVisualizerEngine.InitializeParticles - Particle {i}: X={p.X}, Y={p.Y}, VX={p.VX}, VY={p.VY}, Life={p.Life}, Size={p.Size}"
                    );
            }
            DebugWrite.Line(
                $"AudioVisualizerEngine.InitializeParticles - {_particles.Count} particles initialized."
            );
        }

        private Color HSVtoRGB(float h, float s, float v)
        {
            //DebugWrite.Line($"AudioVisualizerEngine.HSVtoRGB - Input H: {h}, S: {s}, V: {v}");
            h = h - (float)Math.Floor(h); // Ensure h is [0,1)
            s = Math.Max(0f, Math.Min(1f, s)); // Clamp s to [0,1]
            v = Math.Max(0f, Math.Min(1f, v)); // Clamp v to [0,1]

            int hi = Convert.ToInt32(Math.Floor(h * 6)) % 6;
            double f = h * 6 - Math.Floor(h * 6);
            double p_val = v * (1 - s);
            double q_val = v * (1 - f * s);
            double t_val = v * (1 - (1 - f) * s);

            int r,
                gr,
                b;

            if (hi == 0)
            {
                r = (int)(v * 255);
                gr = (int)(t_val * 255);
                b = (int)(p_val * 255);
            }
            else if (hi == 1)
            {
                r = (int)(q_val * 255);
                gr = (int)(v * 255);
                b = (int)(p_val * 255);
            }
            else if (hi == 2)
            {
                r = (int)(p_val * 255);
                gr = (int)(v * 255);
                b = (int)(t_val * 255);
            }
            else if (hi == 3)
            {
                r = (int)(p_val * 255);
                gr = (int)(q_val * 255);
                b = (int)(v * 255);
            }
            else if (hi == 4)
            {
                r = (int)(t_val * 255);
                gr = (int)(p_val * 255);
                b = (int)(v * 255);
            }
            else
            {
                r = (int)(v * 255);
                gr = (int)(p_val * 255);
                b = (int)(q_val * 255);
            }

            Color result = Color.FromArgb(
                255,
                Math.Max(0, Math.Min(255, r)),
                Math.Max(0, Math.Min(255, gr)),
                Math.Max(0, Math.Min(255, b))
            );
            //DebugWrite.Line($"AudioVisualizerEngine.HSVtoRGB - Output RGB: {result}");
            return result;
        }

        private float Lerp(float a, float b, float t)
        {
            //DebugWrite.Line($"AudioVisualizerEngine.Lerp - a: {a}, b: {b}, t: {t}");
            float result = a + (b - a) * t;
            //DebugWrite.Line($"AudioVisualizerEngine.Lerp - result: {result}");
            return result;
        }

        private double GetAudioDuration(string mp3Path)
        {
            DebugWrite.Line(
                $"AudioVisualizerEngine.GetAudioDuration - Getting duration for: {mp3Path}"
            );
            using (var reader = new Mp3FileReader(mp3Path))
            {
                double duration = reader.TotalTime.TotalSeconds;
                DebugWrite.Line(
                    $"AudioVisualizerEngine.GetAudioDuration - Duration: {duration} seconds."
                );
                return duration;
            }
        }

        private async Task MergeAudioVideo(string audioPath, string videoPath)
        {
            string tempOutputPath = Path.Combine(
                Path.GetDirectoryName(videoPath),
                Path.GetFileNameWithoutExtension(videoPath) + "_temp_with_audio.mp4"
            );
            string finalOutputPath = videoPath; // Original video path will be replaced

            DebugWrite.Line(
                $"AudioVisualizerEngine.MergeAudioVideo - Merging Audio: {audioPath} and Video: {videoPath}. Temp Output: {tempOutputPath}"
            );

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments =
                        $"-y -i \"{videoPath}\" -i \"{audioPath}\" -c:v copy -c:a aac -shortest \"{tempOutputPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };

            DebugWrite.Line(
                $"AudioVisualizerEngine.MergeAudioVideo - FFmpeg arguments: {process.StartInfo.Arguments}"
            );

            process.OutputDataReceived += (sender, args) =>
                DebugWrite.Line($"FFmpeg Output: {args.Data}");
            process.ErrorDataReceived += (sender, args) =>
                DebugWrite.Line($"FFmpeg Error: {args.Data}");

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            DebugWrite.Line(
                "AudioVisualizerEngine.MergeAudioVideo - FFmpeg process started. Waiting for exit..."
            );
            await process.WaitForExitAsync(); // Use the new C# 8.0 method if available, otherwise use Task.Run for WaitForExit
            DebugWrite.Line(
                $"AudioVisualizerEngine.MergeAudioVideo - FFmpeg process exited with code: {process.ExitCode}."
            );

            if (process.ExitCode == 0 && File.Exists(tempOutputPath))
            {
                DebugWrite.Line(
                    $"AudioVisualizerEngine.MergeAudioVideo - Merge successful. Replacing original video."
                );
                File.Delete(finalOutputPath); // Delete the video-only file
                File.Move(tempOutputPath, finalOutputPath); // Rename temp file to original name
                DebugWrite.Line(
                    $"AudioVisualizerEngine.MergeAudioVideo - Original video replaced with merged version: {finalOutputPath}"
                );
            }
            else
            {
                DebugWrite.Line(
                    $"AudioVisualizerEngine.MergeAudioVideo - Merge failed or temp output file not found. Exit code: {process.ExitCode}. Temp file exists: {File.Exists(tempOutputPath)}"
                );
                if (File.Exists(tempOutputPath))
                {
                    try
                    {
                        File.Delete(tempOutputPath);
                        DebugWrite.Line(
                            "AudioVisualizerEngine.MergeAudioVideo - Cleaned up temp output file."
                        );
                    }
                    catch (Exception ex)
                    {
                        DebugWrite.Line(
                            $"AudioVisualizerEngine.MergeAudioVideo - Error deleting temp file: {ex.Message}"
                        );
                    }
                }
            }
        }

        private class Particle
        {
            public float X { get; set; }
            public float Y { get; set; }
            public float VX { get; set; }
            public float VY { get; set; }
            public float Life { get; set; }
            public float Size { get; set; }
        }
    }
}
