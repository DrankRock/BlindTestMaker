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
        private float[] _fftBuffer;
        private NAudio.Dsp.Complex[] _fftComplex;
        private readonly object _sampleLock = new object();
        public readonly Random _random = new Random();
        public int _frameCount = 0;
        public float _smoothBass = 0f;
        public float _smoothMid = 0f;
        public float _smoothHigh = 0f;
        private readonly Queue<float[]> _historyBuffer = new Queue<float[]>();
        private readonly int _historySize = 30;
        private List<float> _audioSamples;

        // Particle system for advanced effects
        private List<Particle> _particles = new List<Particle>();

        public AudioVisualizerEngine(
            int width = 1920,
            int height = 1080,
            int fps = 60,
            string outputPath = "output.mp4"
        )
        {
            _width = width;
            _height = height;
            _fps = fps;
            _outputPath = outputPath;
            _libVLC = new LibVLC();
            _fftBuffer = new float[2048];
            _fftComplex = new NAudio.Dsp.Complex[2048];
            _audioSamples = new List<float>();
        }

        public async Task CreateVisualization(
            string mp3Path,
            VisualizationMode mode,
            ColorMode colorMode
        )
        {
            // Load audio data
            await LoadAudioData(mp3Path);

            // Initialize video writer
            var videoWriter = new VideoFileWriter();
            videoWriter.Open(_outputPath, _width, _height, _fps, VideoCodec.H264);

            // Calculate total frames
            var duration = GetAudioDuration(mp3Path);
            var totalFrames = (int)(duration * _fps);

            // Initialize particles if needed
            if (mode == VisualizationMode.ParticleFlow || mode == VisualizationMode.Aurora)
            {
                InitializeParticles(1000);
            }

            // Generate frames
            for (int frame = 0; frame < totalFrames; frame++)
            {
                var bitmap = GenerateFrame(frame, totalFrames, mode, colorMode);
                videoWriter.WriteVideoFrame(bitmap);
                bitmap.Dispose();

                _frameCount++;
            }

            videoWriter.Close();

            // Merge audio with video
            await MergeAudioVideo(mp3Path, _outputPath);
        }

        private async Task LoadAudioData(string mp3Path)
        {
            await Task.Run(() =>
            {
                using (var reader = new Mp3FileReader(mp3Path))
                {
                    var sampleProvider = reader.ToSampleProvider();
                    var samples = new List<float>();
                    var buffer = new float[1024];
                    int samplesRead;

                    while ((samplesRead = sampleProvider.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        samples.AddRange(buffer.Take(samplesRead));
                    }

                    _audioSamples = samples;
                }
            });
        }

        private System.Drawing.Bitmap GenerateFrame(
            int frameIndex,
            int totalFrames,
            VisualizationMode mode,
            ColorMode colorMode
        )
        {
            Bitmap bitmap = new Bitmap(_width, _height);
            using (var g = Graphics.FromImage(bitmap))
            {
                // Black background with slight fade for trailing effect
                g.FillRectangle(new SolidBrush(Color.FromArgb(20, 0, 0, 0)), 0, 0, _width, _height);

                // Get current audio window
                var samplesPerFrame = _audioSamples.Count / totalFrames;
                var startIndex = frameIndex * samplesPerFrame;
                var endIndex = Math.Min(startIndex + samplesPerFrame, _audioSamples.Count);

                if (startIndex < _audioSamples.Count)
                {
                    var currentSamples = new float[endIndex - startIndex];
                    Array.Copy(
                        _audioSamples.ToArray(),
                        startIndex,
                        currentSamples,
                        0,
                        currentSamples.Length
                    );

                    // Perform FFT for frequency analysis
                    PerformFFT(currentSamples);
                    UpdateFrequencyBands();

                    // Update history buffer
                    UpdateHistory(currentSamples);

                    // Draw visualization
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

        private void PerformFFT(float[] samples)
        {
            // Prepare FFT buffer
            for (int i = 0; i < Math.Min(samples.Length, _fftComplex.Length); i++)
            {
                _fftComplex[i].X =
                    samples[i] * (float)FastFourierTransform.HammingWindow(i, _fftComplex.Length);
                _fftComplex[i].Y = 0;
            }

            // Perform FFT
            FastFourierTransform.FFT(true, (int)Math.Log(_fftComplex.Length, 2), _fftComplex);

            // Calculate magnitudes
            for (int i = 0; i < _fftBuffer.Length / 2; i++)
            {
                float real = _fftComplex[i].X;
                float imaginary = _fftComplex[i].Y;
                _fftBuffer[i] = (float)Math.Sqrt(real * real + imaginary * imaginary);
            }
        }

        private void UpdateFrequencyBands()
        {
            // Calculate frequency bands (bass, mid, high)
            float bass = 0,
                mid = 0,
                high = 0;
            int bassEnd = _fftBuffer.Length / 8;
            int midEnd = _fftBuffer.Length / 4;

            for (int i = 0; i < bassEnd; i++)
                bass += _fftBuffer[i];
            for (int i = bassEnd; i < midEnd; i++)
                mid += _fftBuffer[i];
            for (int i = midEnd; i < _fftBuffer.Length / 2; i++)
                high += _fftBuffer[i];

            // Smooth the values
            _smoothBass = Lerp(_smoothBass, bass / bassEnd, 0.3f);
            _smoothMid = Lerp(_smoothMid, mid / (midEnd - bassEnd), 0.3f);
            _smoothHigh = Lerp(_smoothHigh, high / (_fftBuffer.Length / 2 - midEnd), 0.3f);
        }

        private void UpdateHistory(float[] samples)
        {
            _historyBuffer.Enqueue(samples);
            if (_historyBuffer.Count > _historySize)
                _historyBuffer.Dequeue();
        }

        public Color GetColor(ColorMode mode, float intensity, float position = 0)
        {
            switch (mode)
            {
                case ColorMode.Rainbow:
                    return HSVtoRGB((position + _frameCount * 0.001f) % 1f, 1f, intensity);

                case ColorMode.FrequencyBased:
                    float hue = (_smoothBass * 0.1f + _smoothMid * 0.5f + _smoothHigh * 0.9f) % 1f;
                    return HSVtoRGB(hue, 0.8f, intensity);

                case ColorMode.AmplitudeBased:
                    return Color.FromArgb(
                        (int)(intensity * 255),
                        (int)(intensity * 128 + 127),
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
                    // Map frequency content to "emotions" - warm colors for bass, cool for high
                    float emotional =
                        _smoothBass / (_smoothBass + _smoothMid + _smoothHigh + 0.001f);
                    return HSVtoRGB(0.08f + (1f - emotional) * 0.5f, 0.7f, intensity);

                default:
                    return Color.White;
            }
        }

        private void DrawBasicWaveform(Graphics g, float[] samples, ColorMode colorMode)
        {
            var points = new List<PointF>();
            var step = samples.Length / _width;

            for (int x = 0; x < _width; x++)
            {
                int index = x * step;
                if (index < samples.Length)
                {
                    float y = _height / 2f + samples[index] * _height / 4f;
                    points.Add(new PointF(x, y));
                }
            }

            if (points.Count > 1)
            {
                for (int i = 0; i < points.Count - 1; i++)
                {
                    float intensity = Math.Abs(samples[i * step]) * 2f;
                    var color = GetColor(colorMode, intensity, (float)i / points.Count);
                    using (var pen = new Pen(color, 2))
                    {
                        g.DrawLine(pen, points[i], points[i + 1]);
                    }
                }
            }
        }

        private void DrawCircularWave(Graphics g, float[] samples, ColorMode colorMode)
        {
            float centerX = _width / 2f;
            float centerY = _height / 2f;
            float baseRadius = Math.Min(_width, _height) / 4f;

            var points = new List<PointF>();
            int sampleCount = Math.Min(samples.Length, 360);

            for (int i = 0; i < sampleCount; i++)
            {
                float angle = (float)(i * 2 * Math.PI / sampleCount);
                float radius = baseRadius + samples[i] * baseRadius;
                float x = centerX + (float)Math.Cos(angle) * radius;
                float y = centerY + (float)Math.Sin(angle) * radius;
                points.Add(new PointF(x, y));
            }

            if (points.Count > 1)
            {
                for (int i = 0; i < points.Count; i++)
                {
                    int next = (i + 1) % points.Count;
                    float intensity = Math.Abs(samples[i]) * 2f;
                    var color = GetColor(colorMode, intensity, (float)i / points.Count);
                    using (var pen = new Pen(color, 3))
                    {
                        g.DrawLine(pen, points[i], points[next]);
                    }
                }
            }
        }

        private void DrawSphericalPulse(Graphics g, float[] samples, ColorMode colorMode)
        {
            float centerX = _width / 2f;
            float centerY = _height / 2f;

            // Draw multiple concentric circles based on history
            int circleCount = Math.Min(_historyBuffer.Count, 20);
            var histories = _historyBuffer.ToArray();

            for (int h = 0; h < circleCount; h++)
            {
                float alpha = 1f - (float)h / circleCount;
                var historySamples = histories[histories.Length - 1 - h];

                // Calculate average amplitude
                float avgAmplitude = 0;
                for (int i = 0; i < Math.Min(historySamples.Length, 100); i++)
                {
                    avgAmplitude += Math.Abs(historySamples[i]);
                }
                avgAmplitude /= Math.Min(historySamples.Length, 100);

                float radius = 50 + avgAmplitude * 500 + h * 20;
                var color = GetColor(colorMode, alpha * 0.7f, avgAmplitude);

                using (var pen = new Pen(Color.FromArgb((int)(alpha * 255), color), 2))
                {
                    g.DrawEllipse(pen, centerX - radius, centerY - radius, radius * 2, radius * 2);
                }
            }
        }

        private void DrawSpectrumBars(Graphics g, ColorMode colorMode)
        {
            int barCount = 64;
            float barWidth = (float)_width / barCount;

            for (int i = 0; i < barCount; i++)
            {
                int fftIndex = i * (_fftBuffer.Length / 2) / barCount;
                float magnitude = _fftBuffer[fftIndex] * 10f;
                float barHeight = Math.Min(magnitude * _height, _height * 0.8f);

                float x = i * barWidth;
                float y = _height - barHeight;

                var color = GetColor(colorMode, Math.Min(magnitude, 1f), (float)i / barCount);
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
        }

        private void DrawParticleFlow(Graphics g, float[] samples, ColorMode colorMode)
        {
            // Update particles based on audio
            float avgAmplitude = samples.Take(100).Select(Math.Abs).Average();

            // Spawn new particles
            if (avgAmplitude > 0.1f && _particles.Count < 2000)
            {
                for (int i = 0; i < 10; i++)
                {
                    _particles.Add(
                        new Particle
                        {
                            X = _random.Next(_width),
                            Y = _random.Next(_height),
                            VX = (_random.NextSingle() - 0.5f) * avgAmplitude * 20,
                            VY = (_random.NextSingle() - 0.5f) * avgAmplitude * 20,
                            Life = 1f,
                            Size = _random.NextSingle() * 5 + 2,
                        }
                    );
                }
            }

            // Update and draw particles
            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                var p = _particles[i];

                // Update position
                p.X += p.VX;
                p.Y += p.VY;
                p.Life -= 0.01f;

                // Apply audio-reactive forces
                p.VX += (_smoothBass - 0.5f) * 0.5f;
                p.VY += (_smoothHigh - 0.5f) * 0.5f;

                // Remove dead particles
                if (p.Life <= 0 || p.X < 0 || p.X > _width || p.Y < 0 || p.Y > _height)
                {
                    _particles.RemoveAt(i);
                    continue;
                }

                // Draw particle
                var color = GetColor(colorMode, p.Life, p.X / _width);
                using (var brush = new SolidBrush(Color.FromArgb((int)(p.Life * 255), color)))
                {
                    g.FillEllipse(brush, p.X - p.Size / 2, p.Y - p.Size / 2, p.Size, p.Size);
                }
            }
        }

        private void DrawKaleidoscopeWave(Graphics g, float[] samples, ColorMode colorMode)
        {
            float centerX = _width / 2f;
            float centerY = _height / 2f;
            int segments = 8;

            g.TranslateTransform(centerX, centerY);

            for (int seg = 0; seg < segments; seg++)
            {
                g.RotateTransform(360f / segments);

                var points = new List<PointF>();
                int sampleStep = samples.Length / 100;

                for (int i = 0; i < 100; i++)
                {
                    int index = i * sampleStep;
                    if (index < samples.Length)
                    {
                        float r = 50 + i * 3 + samples[index] * 100;
                        float angle = (float)(i * 0.1);
                        float x = r * (float)Math.Cos(angle);
                        float y = r * (float)Math.Sin(angle);
                        points.Add(new PointF(x, y));
                    }
                }

                if (points.Count > 1)
                {
                    for (int i = 0; i < points.Count - 1; i++)
                    {
                        float intensity = Math.Abs(samples[i * sampleStep]) * 2f;
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
        }

        private void DrawDNAHelix(Graphics g, float[] samples, ColorMode colorMode)
        {
            float centerX = _width / 2f;
            int sampleCount = Math.Min(samples.Length, _height / 2);

            for (int i = 0; i < sampleCount; i++)
            {
                float y = i * 2;
                float phase = y * 0.02f + _frameCount * 0.05f;

                // First strand
                float x1 = centerX + (float)Math.Sin(phase) * (100 + samples[i] * 200);
                // Second strand
                float x2 = centerX + (float)Math.Sin(phase + Math.PI) * (100 + samples[i] * 200);

                float intensity = Math.Abs(samples[i]) * 2f;
                var color1 = GetColor(colorMode, intensity, 0);
                var color2 = GetColor(colorMode, intensity, 0.5f);

                // Draw strands
                using (var brush1 = new SolidBrush(color1))
                using (var brush2 = new SolidBrush(color2))
                {
                    g.FillEllipse(brush1, x1 - 5, y - 5, 10, 10);
                    g.FillEllipse(brush2, x2 - 5, y - 5, 10, 10);
                }

                // Draw connections
                if (i % 10 == 0)
                {
                    using (var pen = new Pen(Color.FromArgb(100, color1), 2))
                    {
                        g.DrawLine(pen, x1, y, x2, y);
                    }
                }
            }
        }

        private void DrawAurora(Graphics g, float[] samples, ColorMode colorMode)
        {
            // Create flowing aurora bands
            int bandCount = 5;

            for (int band = 0; band < bandCount; band++)
            {
                var points = new List<PointF>();

                for (int x = 0; x < _width; x += 5)
                {
                    float baseY = _height * 0.3f + band * 50;
                    float wave1 = (float)Math.Sin(x * 0.01f + _frameCount * 0.02f + band) * 50;
                    float wave2 = (float)Math.Sin(x * 0.007f - _frameCount * 0.01f) * 30;

                    int sampleIndex = (x * samples.Length) / _width;
                    if (sampleIndex < samples.Length)
                    {
                        float audioInfluence = samples[sampleIndex] * 100;
                        float y = baseY + wave1 + wave2 + audioInfluence;
                        points.Add(new PointF(x, y));
                    }
                }

                if (points.Count > 2)
                {
                    // Create gradient path
                    using (var path = new System.Drawing.Drawing2D.GraphicsPath())
                    {
                        path.AddCurve(points.ToArray());

                        // Add bottom of screen to create filled area
                        path.AddLine(points.Last(), new PointF(_width, _height));
                        path.AddLine(new PointF(_width, _height), new PointF(0, _height));
                        path.AddLine(new PointF(0, _height), points.First());

                        float intensity = 0.3f + _smoothMid * 0.5f;
                        var color = GetColor(colorMode, intensity, (float)band / bandCount);

                        using (
                            var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                                new PointF(0, 0),
                                new PointF(0, _height),
                                Color.FromArgb(100, color),
                                Color.FromArgb(0, color)
                            )
                        )
                        {
                            g.FillPath(brush, path);
                        }
                    }
                }
            }
        }

        private void InitializeParticles(int count)
        {
            _particles.Clear();
            for (int i = 0; i < count; i++)
            {
                _particles.Add(
                    new Particle
                    {
                        X = _random.Next(_width),
                        Y = _random.Next(_height),
                        VX = (_random.NextSingle() - 0.5f) * 2,
                        VY = (_random.NextSingle() - 0.5f) * 2,
                        Life = _random.NextSingle(),
                        Size = _random.NextSingle() * 5 + 1,
                    }
                );
            }
        }

        private Color HSVtoRGB(float h, float s, float v)
        {
            int hi = Convert.ToInt32(Math.Floor(h * 6)) % 6;
            double f = h * 6 - Math.Floor(h * 6);
            double p = v * (1 - s);
            double q = v * (1 - f * s);
            double t = v * (1 - (1 - f) * s);

            if (hi == 0)
                return Color.FromArgb(255, (int)(v * 255), (int)(t * 255), (int)(p * 255));
            else if (hi == 1)
                return Color.FromArgb(255, (int)(q * 255), (int)(v * 255), (int)(p * 255));
            else if (hi == 2)
                return Color.FromArgb(255, (int)(p * 255), (int)(v * 255), (int)(t * 255));
            else if (hi == 3)
                return Color.FromArgb(255, (int)(p * 255), (int)(q * 255), (int)(v * 255));
            else if (hi == 4)
                return Color.FromArgb(255, (int)(t * 255), (int)(p * 255), (int)(v * 255));
            else
                return Color.FromArgb(255, (int)(v * 255), (int)(p * 255), (int)(q * 255));
        }

        private float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        private double GetAudioDuration(string mp3Path)
        {
            using (var reader = new Mp3FileReader(mp3Path))
            {
                return reader.TotalTime.TotalSeconds;
            }
        }

        private async Task MergeAudioVideo(string audioPath, string videoPath)
        {
            string outputPath = Path.GetFileNameWithoutExtension(videoPath) + "_with_audio.mp4";

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments =
                        $"-i \"{videoPath}\" -i \"{audioPath}\" -c:v copy -c:a aac -shortest \"{outputPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };

            process.Start();
            await process.WaitForExitAsync();

            // Replace original video with merged version
            if (File.Exists(outputPath))
            {
                File.Delete(videoPath);
                File.Move(outputPath, videoPath);
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
