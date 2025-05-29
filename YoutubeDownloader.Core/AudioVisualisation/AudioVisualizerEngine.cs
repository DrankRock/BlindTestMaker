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

namespace YoutubeDownloader.Core.AudioVisualisation
{
    public enum VisualizationMode
    {
        BasicWaveform,
        CircularWave,
        SphericalPulse,
        SpectrumBars,
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

        // Particle system for advanced effects
        private List<Particle> _particles = new List<Particle>();

        public AudioVisualizerEngine(
            int width = 1920,
            int height = 1080,
            int fps = 60,
            string outputPath = "output.mp4"
        )
        {
            Debug.WriteLine(
                $"AudioVisualizerEngine.Constructor - Initializing with Width: {width}, Height: {height}, FPS: {fps}, OutputPath: {outputPath}"
            );
            _width = width;
            _height = height;
            _fps = fps;
            _outputPath = outputPath;
            Debug.WriteLine("AudioVisualizerEngine.Constructor - Initializing LibVLC.");
            _libVLC = new LibVLC();
            _fftBuffer = new float[2048];
            _fftComplex = new NAudio.Dsp.Complex[2048];
            _audioSamples = new List<float>();
            Debug.WriteLine("AudioVisualizerEngine.Constructor - Initialization complete.");
        }

        // Add these constants at the top of AudioVisualizerEngine class
        private const int PROGRESS_LOG_INTERVAL = 60; // Log every 60 frames (1 second at 60fps)
        private const int DETAILED_LOG_INTERVAL = 600; // Detailed logs every 600 frames (10 seconds)

        public async Task CreateVisualization(
            string mp3Path,
            VisualizationMode mode,
            ColorMode colorMode
        )
        {
            Debug.WriteLine(
                $"AudioVisualizerEngine.CreateVisualization - Starting visualization for MP3: {mp3Path}, Mode: {mode}, ColorMode: {colorMode}"
            );

            await LoadAudioData(mp3Path);
            Debug.WriteLine("AudioVisualizerEngine.CreateVisualization - Audio data loaded.");

            var videoWriter = new VideoFileWriter();
            videoWriter.Open(_outputPath, _width, _height, _fps, VideoCodec.H264);
            Debug.WriteLine(
                $"AudioVisualizerEngine.CreateVisualization - Video writer opened for output: {_outputPath}"
            );

            var duration = GetAudioDuration(mp3Path);
            var totalFrames = (int)(duration * _fps);
            Debug.WriteLine(
                $"AudioVisualizerEngine.CreateVisualization - Audio duration: {duration:F1}s, Total frames: {totalFrames}"
            );

            if (mode == VisualizationMode.ParticleFlow || mode == VisualizationMode.Aurora)
            {
                InitializeParticles(1000);
            }

            Debug.WriteLine(
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

                    Debug.WriteLine(
                        $"PROGRESS: Frame {frame + 1:N0}/{totalFrames:N0} ({progress:F1}%) | "
                            + $"Speed: {framesPerSecond:F1} fps | "
                            + $"Elapsed: {elapsed:mm\\:ss} | "
                            + $"ETA: {estimatedTimeLeft:mm\\:ss}"
                    );
                }

                // Detailed logging every 10 seconds
                if (frame % DETAILED_LOG_INTERVAL == 0 && frame > 0)
                {
                    Debug.WriteLine(
                        $"DETAIL: Bass={_smoothBass:F3}, Mid={_smoothMid:F3}, High={_smoothHigh:F3} | "
                            + $"Particles={_particles.Count} | "
                            + $"Memory: {GC.GetTotalMemory(false) / 1024 / 1024} MB"
                    );
                }
            }

            var totalTime = DateTime.Now - startTime;
            Debug.WriteLine(
                $"COMPLETE: Frame generation finished in {totalTime:mm\\:ss} | "
                    + $"Average: {totalFrames / totalTime.TotalSeconds:F1} fps"
            );

            videoWriter.Close();
            await MergeAudioVideo(mp3Path, _outputPath);
            Debug.WriteLine(
                $"AudioVisualizerEngine.CreateVisualization - Visualization complete: {_outputPath}"
            );
        }

        public async Task LoadAudioData(string mp3Path)
        {
            Debug.WriteLine(
                $"AudioVisualizerEngine.LoadAudioData - Starting to load audio data from: {mp3Path}"
            );
            await Task.Run(() =>
            {
                Debug.WriteLine(
                    $"AudioVisualizerEngine.LoadAudioData - Task started for reading MP3: {mp3Path}"
                );
                using (var reader = new Mp3FileReader(mp3Path))
                {
                    Debug.WriteLine(
                        $"AudioVisualizerEngine.LoadAudioData - Mp3FileReader opened. Sample rate: {reader.WaveFormat.SampleRate}, Channels: {reader.WaveFormat.Channels}"
                    );
                    var sampleProvider = reader.ToSampleProvider();
                    var samples = new List<float>();
                    var buffer = new float[1024];
                    int samplesRead;
                    int totalSamplesRead = 0;

                    Debug.WriteLine(
                        "AudioVisualizerEngine.LoadAudioData - Starting to read samples."
                    );
                    while ((samplesRead = sampleProvider.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        samples.AddRange(buffer.Take(samplesRead));
                        totalSamplesRead += samplesRead;
                        Debug.WriteLine(
                            $"AudioVisualizerEngine.LoadAudioData - Read {samplesRead} samples. Total samples in list: {samples.Count}"
                        );
                    }
                    Debug.WriteLine(
                        $"AudioVisualizerEngine.LoadAudioData - Finished reading samples. Total samples read from file: {totalSamplesRead}"
                    );
                    _audioSamples = samples;
                    Debug.WriteLine(
                        $"AudioVisualizerEngine.LoadAudioData - _audioSamples populated with {samples.Count} samples."
                    );
                }
            });
            Debug.WriteLine(
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
                Debug.WriteLine(
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
                            DrawBasicWaveform(g, currentSamples, colorMode);
                            break;
                        case VisualizationMode.CircularWave:
                            DrawCircularWave(g, currentSamples, colorMode);
                            break;
                        case VisualizationMode.SphericalPulse:
                            DrawSphericalPulse(g, currentSamples, colorMode);
                            break;
                        case VisualizationMode.SpectrumBars:
                            DrawSpectrumBars(g, colorMode);
                            break;
                        case VisualizationMode.ParticleFlow:
                            DrawParticleFlow(g, currentSamples, colorMode);
                            break;
                        case VisualizationMode.KaleidoscopeWave:
                            DrawKaleidoscopeWave(g, currentSamples, colorMode);
                            break;
                        case VisualizationMode.DNA_Helix:
                            DrawDNAHelix(g, currentSamples, colorMode);
                            break;
                        case VisualizationMode.Aurora:
                            DrawAurora(g, currentSamples, colorMode);
                            break;
                    }
                }
            }
            return bitmap;
        }

        public void PerformFFT(float[] samples)
        {
            Debug.WriteLine(
                $"AudioVisualizerEngine.PerformFFT - Performing FFT on {samples.Length} samples."
            );
            if (samples.Length == 0)
            {
                Debug.WriteLine(
                    "AudioVisualizerEngine.PerformFFT - No samples to perform FFT on. Clearing _fftBuffer."
                );
                Array.Clear(_fftBuffer, 0, _fftBuffer.Length); // Clear previous FFT data if no new samples
                return;
            }

            int fftLength = _fftComplex.Length;
            Debug.WriteLine($"AudioVisualizerEngine.PerformFFT - FFT Complex Length: {fftLength}");

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
            Debug.WriteLine(
                "AudioVisualizerEngine.PerformFFT - FFT buffer prepared with Hamming window and zero-padding if necessary."
            );

            FastFourierTransform.FFT(true, (int)Math.Log(fftLength, 2), _fftComplex);
            Debug.WriteLine("AudioVisualizerEngine.PerformFFT - FFT executed.");

            for (int i = 0; i < _fftBuffer.Length / 2; i++)
            {
                float real = _fftComplex[i].X;
                float imaginary = _fftComplex[i].Y;
                _fftBuffer[i] = (float)Math.Sqrt(real * real + imaginary * imaginary);
                if (i < 5)
                    Debug.WriteLine(
                        $"AudioVisualizerEngine.PerformFFT - _fftBuffer[{i}] Magnitude: {_fftBuffer[i]} (Real: {real}, Imaginary: {imaginary})"
                    );
            }
            Debug.WriteLine(
                "AudioVisualizerEngine.PerformFFT - Magnitudes calculated and stored in _fftBuffer."
            );
        }

        public void UpdateFrequencyBands()
        {
            Debug.WriteLine(
                "AudioVisualizerEngine.UpdateFrequencyBands - Updating frequency bands."
            );
            float bass = 0,
                mid = 0,
                high = 0;
            int bassEnd = _fftBuffer.Length / 8; // Example: 2048/8 = 256
            int midEnd = _fftBuffer.Length / 4; // Example: 2048/4 = 512
            // Max useful index is _fftBuffer.Length / 2 - 1. For 2048, it's 1023.

            Debug.WriteLine(
                $"AudioVisualizerEngine.UpdateFrequencyBands - bassEnd index: {bassEnd}, midEnd index: {midEnd}, fftBuffer half length: {_fftBuffer.Length / 2}"
            );

            for (int i = 0; i < bassEnd; i++)
                bass += _fftBuffer[i];
            for (int i = bassEnd; i < midEnd; i++)
                mid += _fftBuffer[i];
            for (int i = midEnd; i < _fftBuffer.Length / 2; i++)
                high += _fftBuffer[i];

            Debug.WriteLine(
                $"AudioVisualizerEngine.UpdateFrequencyBands - Raw sums: Bass={bass}, Mid={mid}, High={high}"
            );

            float bassAvg = (bassEnd > 0) ? bass / bassEnd : 0;
            float midAvg = (midEnd - bassEnd > 0) ? mid / (midEnd - bassEnd) : 0;
            float highAvg =
                (_fftBuffer.Length / 2 - midEnd > 0) ? high / (_fftBuffer.Length / 2 - midEnd) : 0;

            Debug.WriteLine(
                $"AudioVisualizerEngine.UpdateFrequencyBands - Raw averages: BassAvg={bassAvg}, MidAvg={midAvg}, HighAvg={highAvg}"
            );

            _smoothBass = Lerp(_smoothBass, bassAvg, 0.3f);
            _smoothMid = Lerp(_smoothMid, midAvg, 0.3f);
            _smoothHigh = Lerp(_smoothHigh, highAvg, 0.3f);

            Debug.WriteLine(
                $"AudioVisualizerEngine.UpdateFrequencyBands - Smoothed values: _smoothBass={_smoothBass}, _smoothMid={_smoothMid}, _smoothHigh={_smoothHigh}"
            );
        }

        private void UpdateHistory(float[] samples)
        {
            Debug.WriteLine(
                $"AudioVisualizerEngine.UpdateHistory - Updating history with {samples.Length} samples."
            );
            _historyBuffer.Enqueue((float[])samples.Clone()); // Enqueue a copy
            Debug.WriteLine(
                $"AudioVisualizerEngine.UpdateHistory - Enqueued. History buffer size: {_historyBuffer.Count}"
            );
            if (_historyBuffer.Count > _historySize)
            {
                _historyBuffer.Dequeue();
                Debug.WriteLine(
                    $"AudioVisualizerEngine.UpdateHistory - Dequeued. History buffer size: {_historyBuffer.Count}"
                );
            }
            Debug.WriteLine("AudioVisualizerEngine.UpdateHistory - History update complete.");
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

        private void DrawBasicWaveform(Graphics g, float[] samples, ColorMode colorMode)
        {
            Debug.WriteLine(
                $"AudioVisualizerEngine.DrawBasicWaveform - Drawing with {samples.Length} samples."
            );
            if (samples.Length == 0)
            {
                Debug.WriteLine("AudioVisualizerEngine.DrawBasicWaveform - No samples to draw.");
                return;
            }

            var points = new List<PointF>();
            var step = Math.Max(1, samples.Length / _width); // Ensure step is at least 1

            for (int x = 0; x < _width; x++)
            {
                int index = x * step;
                if (index < samples.Length)
                {
                    float y = _height / 2f + samples[index] * _height / 4f;
                    points.Add(new PointF(x, y));
                }
                else if (points.Count > 0) // If out of samples, add last point again to complete line to edge if necessary
                {
                    points.Add(new PointF(x, points.Last().Y));
                }
            }
            Debug.WriteLine(
                $"AudioVisualizerEngine.DrawBasicWaveform - Generated {points.Count} points for waveform. Step was {step}"
            );

            if (points.Count > 1)
            {
                for (int i = 0; i < points.Count - 1; i++)
                {
                    int sampleIndex = i * step;
                    float intensity =
                        (sampleIndex < samples.Length) ? Math.Abs(samples[sampleIndex]) * 2f : 0.5f;
                    var color = GetColor(colorMode, intensity, (float)i / points.Count);
                    using (var pen = new Pen(color, 2))
                    {
                        g.DrawLine(pen, points[i], points[i + 1]);
                    }
                }
                Debug.WriteLine(
                    $"AudioVisualizerEngine.DrawBasicWaveform - Drawn {points.Count - 1} line segments."
                );
            }
            else
            {
                Debug.WriteLine(
                    "AudioVisualizerEngine.DrawBasicWaveform - Not enough points to draw lines."
                );
            }
        }

        private void DrawCircularWave(Graphics g, float[] samples, ColorMode colorMode)
        {
            Debug.WriteLine(
                $"AudioVisualizerEngine.DrawCircularWave - Drawing with {samples.Length} samples."
            );
            if (samples.Length == 0)
            {
                Debug.WriteLine("AudioVisualizerEngine.DrawCircularWave - No samples to draw.");
                return;
            }

            float centerX = _width / 2f;
            float centerY = _height / 2f;
            float baseRadius = Math.Min(_width, _height) / 4f;
            Debug.WriteLine(
                $"AudioVisualizerEngine.DrawCircularWave - Center: ({centerX},{centerY}), BaseRadius: {baseRadius}"
            );

            var points = new List<PointF>();
            int sampleCount = Math.Min(samples.Length, 360); // Use up to 360 samples for a smooth circle
            Debug.WriteLine(
                $"AudioVisualizerEngine.DrawCircularWave - Will use {sampleCount} samples for the circle."
            );

            for (int i = 0; i < sampleCount; i++)
            {
                float angle = (float)(i * 2 * Math.PI / sampleCount);
                // Ensure sample index is within bounds if samples.Length < sampleCount
                int currentSampleIndex =
                    (samples.Length == sampleCount) ? i : (i * samples.Length / sampleCount);

                float radius = baseRadius + samples[currentSampleIndex] * baseRadius;
                float x = centerX + (float)Math.Cos(angle) * radius;
                float y = centerY + (float)Math.Sin(angle) * radius;
                points.Add(new PointF(x, y));
                if (i < 5)
                    Debug.WriteLine(
                        $"AudioVisualizerEngine.DrawCircularWave - Point {i}: Angle={angle}, SampleVal={samples[currentSampleIndex]}, Radius={radius}, Pos=({x},{y})"
                    );
            }
            Debug.WriteLine(
                $"AudioVisualizerEngine.DrawCircularWave - Generated {points.Count} points for circular wave."
            );

            if (points.Count > 1)
            {
                for (int i = 0; i < points.Count; i++)
                {
                    int next = (i + 1) % points.Count;
                    int currentSampleIndex =
                        (samples.Length == sampleCount) ? i : (i * samples.Length / sampleCount);
                    float intensity = Math.Abs(samples[currentSampleIndex]) * 2f;
                    var color = GetColor(colorMode, intensity, (float)i / points.Count);
                    using (var pen = new Pen(color, 3))
                    {
                        g.DrawLine(pen, points[i], points[next]);
                    }
                }
                Debug.WriteLine(
                    $"AudioVisualizerEngine.DrawCircularWave - Drawn {points.Count} line segments for the circle."
                );
            }
            else
            {
                Debug.WriteLine(
                    "AudioVisualizerEngine.DrawCircularWave - Not enough points to draw lines."
                );
            }
        }

        private void DrawSphericalPulse(Graphics g, float[] samples, ColorMode colorMode)
        {
            Debug.WriteLine(
                $"AudioVisualizerEngine.DrawSphericalPulse - Drawing with {samples.Length} samples."
            );
            if (samples.Length == 0 && _historyBuffer.Count == 0)
            {
                Debug.WriteLine(
                    "AudioVisualizerEngine.DrawSphericalPulse - No samples and no history to draw."
                );
                return;
            }

            float centerX = _width / 2f;
            float centerY = _height / 2f;

            int circleCount = Math.Min(_historyBuffer.Count, 20);
            var histories = _historyBuffer.ToArray(); // Get a snapshot
            Debug.WriteLine(
                $"AudioVisualizerEngine.DrawSphericalPulse - Center: ({centerX},{centerY}), Drawing {circleCount} historical circles."
            );

            for (int h = 0; h < circleCount; h++)
            {
                float alpha = 1f - (float)h / circleCount;
                // Get samples from history, ensure we don't go out of bounds
                var historySamples = histories[(histories.Length - 1) - h];
                if (historySamples == null || historySamples.Length == 0)
                {
                    Debug.WriteLine(
                        $"AudioVisualizerEngine.DrawSphericalPulse - History samples at index {h} is null or empty. Skipping."
                    );
                    continue;
                }

                float avgAmplitude = 0;
                int samplesToAverage = Math.Min(historySamples.Length, 100);
                if (samplesToAverage == 0)
                {
                    Debug.WriteLine(
                        $"AudioVisualizerEngine.DrawSphericalPulse - No history samples to average for circle {h}. Skipping."
                    );
                    continue;
                }

                for (int i = 0; i < samplesToAverage; i++)
                {
                    avgAmplitude += Math.Abs(historySamples[i]);
                }
                avgAmplitude /= samplesToAverage;

                float radius = 50 + avgAmplitude * 500 + h * 20;
                var color = GetColor(colorMode, alpha * 0.7f, avgAmplitude);
                Debug.WriteLine(
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
            Debug.WriteLine("AudioVisualizerEngine.DrawSphericalPulse - Finished drawing pulses.");
        }

        private void DrawSpectrumBars(Graphics g, ColorMode colorMode)
        {
            Debug.WriteLine("AudioVisualizerEngine.DrawSpectrumBars - Drawing spectrum bars.");
            int barCount = 64;
            float barWidth = (float)_width / barCount;
            Debug.WriteLine(
                $"AudioVisualizerEngine.DrawSpectrumBars - BarCount: {barCount}, BarWidth: {barWidth}"
            );

            for (int i = 0; i < barCount; i++)
            {
                // Ensure fftIndex is within the valid range of _fftBuffer (up to _fftBuffer.Length / 2 - 1)
                int fftIndex = Math.Min(
                    (i * (_fftBuffer.Length / 2)) / barCount,
                    (_fftBuffer.Length / 2) - 1
                );
                if (fftIndex < 0)
                    fftIndex = 0; // Should not happen with proper barCount/fftBuffer length

                float magnitude = _fftBuffer[fftIndex] * 10f;
                float barHeight = Math.Min(magnitude * _height * 0.75f, _height * 0.8f); // Adjusted scaling
                barHeight = Math.Max(0, barHeight); // Ensure barHeight is not negative

                float x = i * barWidth;
                float y = _height - barHeight;

                var color = GetColor(colorMode, Math.Min(magnitude, 1f), (float)i / barCount);
                if (i == 0 || i == barCount - 1)
                    Debug.WriteLine(
                        $"AudioVisualizerEngine.DrawSpectrumBars - Bar {i}: FFTIndex={fftIndex}, Magnitude={_fftBuffer[fftIndex]}, CalcMag={magnitude}, BarHeight={barHeight}, X={x}, Y={y}, Color={color}"
                    );

                using (var brush = new SolidBrush(color))
                {
                    g.FillRectangle(brush, x, y, barWidth - 2, barHeight);
                }

                // Add glow effect
                using (var glowBrush = new SolidBrush(Color.FromArgb(50, color)))
                {
                    g.FillRectangle(glowBrush, x - 2, y - 5, barWidth + 2, barHeight + 10);
                }
            }
            Debug.WriteLine(
                "AudioVisualizerEngine.DrawSpectrumBars - Finished drawing spectrum bars."
            );
        }

        private void DrawParticleFlow(Graphics g, float[] samples, ColorMode colorMode)
        {
            Debug.WriteLine(
                $"AudioVisualizerEngine.DrawParticleFlow - Drawing with {samples.Length} samples. Particle count: {_particles.Count}"
            );
            if (samples.Length == 0)
            {
                Debug.WriteLine(
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
            Debug.WriteLine(
                $"AudioVisualizerEngine.DrawParticleFlow - AvgAmplitude: {avgAmplitude}"
            );

            // Spawn new particles
            if (avgAmplitude > 0.1f && _particles.Count < 2000)
            {
                int particlesToSpawn = (int)(avgAmplitude * 20); // Spawn more based on amplitude
                particlesToSpawn = Math.Min(particlesToSpawn, 20); // Cap spawn rate
                Debug.WriteLine(
                    $"AudioVisualizerEngine.DrawParticleFlow - Spawning {particlesToSpawn} new particles."
                );
                for (int i = 0; i < particlesToSpawn; i++)
                {
                    var newParticle = new Particle
                    {
                        X = _random.Next(_width),
                        Y = _random.Next(_height),
                        VX = (_random.NextSingle() - 0.5f) * avgAmplitude * 20,
                        VY = (_random.NextSingle() - 0.5f) * avgAmplitude * 20,
                        Life = 1f,
                        Size = _random.NextSingle() * 5 + 2,
                    };
                    _particles.Add(newParticle);
                    if (i == 0)
                        Debug.WriteLine(
                            $"AudioVisualizerEngine.DrawParticleFlow - New particle: X={newParticle.X}, Y={newParticle.Y}, VX={newParticle.VX}, VY={newParticle.VY}"
                        );
                }
            }

            Debug.WriteLine(
                $"AudioVisualizerEngine.DrawParticleFlow - Updating and drawing {_particles.Count} particles."
            );
            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                var p = _particles[i];

                p.X += p.VX;
                p.Y += p.VY;
                p.Life -= 0.01f;

                // Dampen velocity
                p.VX *= 0.98f;
                p.VY *= 0.98f;

                // Apply audio-reactive forces (more subtle)
                p.VX += (_smoothBass - 0.3f) * 0.1f * Math.Sign(p.VX); // Apply force relative to current velocity direction or a base
                p.VY += (_smoothMid - 0.3f) * 0.1f * Math.Sign(p.VY);

                if (
                    p.Life <= 0
                    || p.X < -p.Size
                    || p.X > _width + p.Size
                    || p.Y < -p.Size
                    || p.Y > _height + p.Size
                )
                {
                    _particles.RemoveAt(i);
                    // Debug.WriteLine($"AudioVisualizerEngine.DrawParticleFlow - Particle removed. Reason: Life={p.Life}, X={p.X}, Y={p.Y}");
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
            Debug.WriteLine(
                "AudioVisualizerEngine.DrawParticleFlow - Finished particle flow frame."
            );
        }

        private void DrawKaleidoscopeWave(Graphics g, float[] samples, ColorMode colorMode)
        {
            Debug.WriteLine(
                $"AudioVisualizerEngine.DrawKaleidoscopeWave - Drawing with {samples.Length} samples."
            );
            if (samples.Length == 0)
            {
                Debug.WriteLine("AudioVisualizerEngine.DrawKaleidoscopeWave - No samples to draw.");
                return;
            }

            float centerX = _width / 2f;
            float centerY = _height / 2f;
            int segments = 8;
            Debug.WriteLine(
                $"AudioVisualizerEngine.DrawKaleidoscopeWave - Center: ({centerX},{centerY}), Segments: {segments}"
            );

            g.TranslateTransform(centerX, centerY);
            Debug.WriteLine("AudioVisualizerEngine.DrawKaleidoscopeWave - Translated to center.");

            for (int seg = 0; seg < segments; seg++)
            {
                g.RotateTransform(360f / segments);
                // Debug.WriteLine($"AudioVisualizerEngine.DrawKaleidoscopeWave - Segment {seg}: Rotated by {360f / segments} degrees.");

                var points = new List<PointF>();
                int numPointsInSegment = 100;
                // Ensure sampleStep is at least 1, and index doesn't go out of bounds
                int sampleStep = Math.Max(1, samples.Length / numPointsInSegment);

                for (int i = 0; i < numPointsInSegment; i++)
                {
                    int index = i * sampleStep;
                    if (index < samples.Length)
                    {
                        float r =
                            50
                            + i * (_width / (float)(numPointsInSegment * 4))
                            + samples[index] * 100; // Scale radius based on width/points
                        float angle = (float)(i * 0.1); // Angle relative to segment's current orientation
                        float x = r * (float)Math.Cos(angle);
                        float y = r * (float)Math.Sin(angle);
                        points.Add(new PointF(x, y));
                    }
                    else if (points.Any())
                    { // Add last point if out of samples
                        points.Add(points.Last());
                    }
                }
                Debug.WriteLine(
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
                            (float)i / points.Count + (float)seg / segments
                        );
                        using (var pen = new Pen(color, 2))
                        {
                            g.DrawLine(pen, points[i], points[i + 1]);
                        }
                    }
                }
            }

            g.ResetTransform();
            Debug.WriteLine(
                "AudioVisualizerEngine.DrawKaleidoscopeWave - Transform reset. Drawing complete."
            );
        }

        private void DrawDNAHelix(Graphics g, float[] samples, ColorMode colorMode)
        {
            Debug.WriteLine(
                $"AudioVisualizerEngine.DrawDNAHelix - Drawing with {samples.Length} samples."
            );
            if (samples.Length == 0)
            {
                Debug.WriteLine("AudioVisualizerEngine.DrawDNAHelix - No samples to draw.");
                return;
            }

            float centerX = _width / 2f;
            int numPointsInHelix = Math.Min(samples.Length, _height / 2); // Number of points along the Y axis
            float yStep = (float)_height / numPointsInHelix; // Ensure helix spans height
            Debug.WriteLine(
                $"AudioVisualizerEngine.DrawDNAHelix - CenterX: {centerX}, Points in Helix: {numPointsInHelix}, Y-Step: {yStep}"
            );

            for (int i = 0; i < numPointsInHelix; i++)
            {
                float y = i * yStep;
                float phase = y * 0.02f + _frameCount * 0.05f;

                // Ensure sample index is within bounds
                int sampleIndex = i * samples.Length / numPointsInHelix; // Map i to sample index

                float x1 =
                    centerX + (float)Math.Sin(phase) * (50 + samples[sampleIndex] * (_width / 8f)); // Scale radius
                float x2 =
                    centerX
                    + (float)Math.Sin(phase + Math.PI)
                        * (50 + samples[sampleIndex] * (_width / 8f));

                float intensity = Math.Abs(samples[sampleIndex]) * 2f;
                var color1 = GetColor(colorMode, intensity, phase % 1f); // Vary color by phase for strand 1
                var color2 = GetColor(colorMode, intensity, (phase + 0.5f) % 1f); // Vary color for strand 2

                if (i < 2)
                    Debug.WriteLine(
                        $"AudioVisualizerEngine.DrawDNAHelix - Point {i}: Y={y}, Phase={phase}, SampleVal={samples[sampleIndex]}, X1={x1}, X2={x2}"
                    );

                using (var brush1 = new SolidBrush(color1))
                using (var brush2 = new SolidBrush(color2))
                {
                    g.FillEllipse(brush1, x1 - 5, y - 5, 10, 10);
                    g.FillEllipse(brush2, x2 - 5, y - 5, 10, 10);
                }

                if (i % (numPointsInHelix / 20 + 1) == 0) // Adjust connection frequency
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
            Debug.WriteLine("AudioVisualizerEngine.DrawDNAHelix - Finished drawing helix.");
        }

        private void DrawAurora(Graphics g, float[] samples, ColorMode colorMode)
        {
            Debug.WriteLine(
                $"AudioVisualizerEngine.DrawAurora - Drawing with {samples.Length} samples."
            );
            if (samples.Length == 0)
            {
                Debug.WriteLine("AudioVisualizerEngine.DrawAurora - No samples to draw.");
                return;
            }

            int bandCount = 5 + (int)(_smoothMid * 5); // Dynamic band count
            bandCount = Math.Max(3, Math.Min(bandCount, 10)); // Clamp band count
            Debug.WriteLine($"AudioVisualizerEngine.DrawAurora - BandCount: {bandCount}");

            for (int band = 0; band < bandCount; band++)
            {
                var points = new List<PointF>();
                int pointStep = 10; // Density of points for the curve

                for (int x = 0; x <= _width; x += pointStep) // ensure x goes to _width
                {
                    float baseY = _height * (0.2f + (float)band / bandCount * 0.5f); // Spread bands more evenly
                    float wave1 =
                        (float)
                            Math.Sin(
                                x * (0.005f + band * 0.001f)
                                    + _frameCount * (0.01f + _smoothBass * 0.01f)
                                    + band
                            ) * (_height * 0.1f);
                    float wave2 =
                        (float)
                            Math.Sin(
                                x * (0.003f - band * 0.0005f)
                                    - _frameCount * (0.005f + _smoothHigh * 0.01f)
                                    + band * 0.5f
                            ) * (_height * 0.05f);

                    int sampleIndex = Math.Min(samples.Length - 1, (x * samples.Length) / _width); // Ensure valid index
                    if (sampleIndex < 0)
                        sampleIndex = 0;

                    float audioInfluence = samples[sampleIndex] * (_height * 0.15f); // Audio influences height
                    float y = baseY + wave1 + wave2 + audioInfluence;
                    y = Math.Max(0, Math.Min(_height, y)); // Clamp Y to screen bounds
                    points.Add(new PointF(x, y));
                }
                if (band == 0 && points.Count > 0)
                    Debug.WriteLine(
                        $"AudioVisualizerEngine.DrawAurora - Band {band}: Generated {points.Count} points. First point: {points.First()}, Last point: {points.Last()}"
                    );

                if (points.Count > 2)
                {
                    using (var path = new System.Drawing.Drawing2D.GraphicsPath())
                    {
                        path.AddCurve(points.ToArray());
                        Debug.WriteLine(
                            $"AudioVisualizerEngine.DrawAurora - Band {band}: Curve added to path with {points.Count} points."
                        );

                        // Ensure path closes properly to form a fillable area
                        if (points.Any())
                        {
                            path.AddLine(points.Last(), new PointF(_width, _height));
                            path.AddLine(new PointF(_width, _height), new PointF(0, _height));
                            path.AddLine(new PointF(0, _height), points.First());
                            Debug.WriteLine(
                                $"AudioVisualizerEngine.DrawAurora - Band {band}: Path closed for filling."
                            );
                        }

                        float intensity = 0.2f + (_smoothMid + _smoothBass) * 0.4f; // Intensity based on mid and bass
                        intensity = Math.Min(1f, Math.Max(0.1f, intensity)); // Clamp intensity

                        var baseColor = GetColor(
                            colorMode,
                            intensity,
                            (float)band / bandCount + _frameCount * 0.002f
                        ); // Time-varying color
                        Color topColor = Color.FromArgb(
                            Math.Min(255, Math.Max(0, (int)(intensity * 150))),
                            baseColor
                        ); // More transparent at top
                        Color bottomColor = Color.FromArgb(0, baseColor); // Fully transparent at bottom

                        if (points.First().Y < _height && points.Last().Y < _height) // Only draw if reasonable
                        {
                            using (
                                var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                                    new PointF(0, points.Min(p => p.Y)), // Gradient starts from the top of the curve
                                    new PointF(0, _height), // Gradient ends at the bottom of the screen
                                    topColor,
                                    bottomColor
                                )
                            )
                            {
                                g.FillPath(brush, path);
                                Debug.WriteLine(
                                    $"AudioVisualizerEngine.DrawAurora - Band {band}: Path filled. Intensity: {intensity}, TopColor: {topColor}"
                                );
                            }
                        }
                        else
                        {
                            Debug.WriteLine(
                                $"AudioVisualizerEngine.DrawAurora - Band {band}: Skipped filling due to extreme Y values. MinY: {points.Min(p => p.Y)}, MaxY: {points.Max(p => p.Y)}"
                            );
                        }
                    }
                }
                else
                {
                    Debug.WriteLine(
                        $"AudioVisualizerEngine.DrawAurora - Band {band}: Not enough points to draw curve ({points.Count})."
                    );
                }
            }
            Debug.WriteLine("AudioVisualizerEngine.DrawAurora - Finished drawing aurora bands.");
        }

        private void InitializeParticles(int count)
        {
            Debug.WriteLine(
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
                    Debug.WriteLine(
                        $"AudioVisualizerEngine.InitializeParticles - Particle {i}: X={p.X}, Y={p.Y}, VX={p.VX}, VY={p.VY}, Life={p.Life}, Size={p.Size}"
                    );
            }
            Debug.WriteLine(
                $"AudioVisualizerEngine.InitializeParticles - {_particles.Count} particles initialized."
            );
        }

        private Color HSVtoRGB(float h, float s, float v)
        {
            // Debug.WriteLine($"AudioVisualizerEngine.HSVtoRGB - Input H: {h}, S: {s}, V: {v}");
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
            // Debug.WriteLine($"AudioVisualizerEngine.HSVtoRGB - Output RGB: {result}");
            return result;
        }

        private float Lerp(float a, float b, float t)
        {
            // Debug.WriteLine($"AudioVisualizerEngine.Lerp - a: {a}, b: {b}, t: {t}");
            float result = a + (b - a) * t;
            // Debug.WriteLine($"AudioVisualizerEngine.Lerp - result: {result}");
            return result;
        }

        private double GetAudioDuration(string mp3Path)
        {
            Debug.WriteLine(
                $"AudioVisualizerEngine.GetAudioDuration - Getting duration for: {mp3Path}"
            );
            using (var reader = new Mp3FileReader(mp3Path))
            {
                double duration = reader.TotalTime.TotalSeconds;
                Debug.WriteLine(
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

            Debug.WriteLine(
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

            Debug.WriteLine(
                $"AudioVisualizerEngine.MergeAudioVideo - FFmpeg arguments: {process.StartInfo.Arguments}"
            );

            process.OutputDataReceived += (sender, args) =>
                Debug.WriteLine($"FFmpeg Output: {args.Data}");
            process.ErrorDataReceived += (sender, args) =>
                Debug.WriteLine($"FFmpeg Error: {args.Data}");

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            Debug.WriteLine(
                "AudioVisualizerEngine.MergeAudioVideo - FFmpeg process started. Waiting for exit..."
            );
            await process.WaitForExitAsync(); // Use the new C# 8.0 method if available, otherwise use Task.Run for WaitForExit
            Debug.WriteLine(
                $"AudioVisualizerEngine.MergeAudioVideo - FFmpeg process exited with code: {process.ExitCode}."
            );

            if (process.ExitCode == 0 && File.Exists(tempOutputPath))
            {
                Debug.WriteLine(
                    $"AudioVisualizerEngine.MergeAudioVideo - Merge successful. Replacing original video."
                );
                File.Delete(finalOutputPath); // Delete the video-only file
                File.Move(tempOutputPath, finalOutputPath); // Rename temp file to original name
                Debug.WriteLine(
                    $"AudioVisualizerEngine.MergeAudioVideo - Original video replaced with merged version: {finalOutputPath}"
                );
            }
            else
            {
                Debug.WriteLine(
                    $"AudioVisualizerEngine.MergeAudioVideo - Merge failed or temp output file not found. Exit code: {process.ExitCode}. Temp file exists: {File.Exists(tempOutputPath)}"
                );
                if (File.Exists(tempOutputPath))
                {
                    try
                    {
                        File.Delete(tempOutputPath);
                        Debug.WriteLine(
                            "AudioVisualizerEngine.MergeAudioVideo - Cleaned up temp output file."
                        );
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
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
