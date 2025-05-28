using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YoutubeDownloader.Core.AudioVisualisation
{
    // Additional visualization modes
    public class AdvancedVisualizer : AudioVisualizerEngine
    {
        private float[,] _waveformGrid;
        private List<LightBeam> _lightBeams = new List<LightBeam>();
        private float _rotationAngle = 0f;
        private List<RippleEffect> _ripples = new List<RippleEffect>();

        public AdvancedVisualizer(int width, int height, int fps, string outputPath)
            : base(width, height, fps, outputPath)
        {
            _waveformGrid = new float[50, 50];
            InitializeLightBeams();
        }

        public void DrawMatrixRain(Graphics g, float[] samples, ColorMode colorMode)
        {
            // Matrix-style falling characters influenced by audio
            int columns = _width / 20;

            for (int col = 0; col < columns; col++)
            {
                float x = col * 20;
                float audioInfluence = samples[col % samples.Length] * 10;

                for (int row = 0; row < _height / 20; row++)
                {
                    float y = row * 20 + _frameCount * 2 % 20;
                    float intensity = (float)row / (_height / 20) + audioInfluence;

                    if (_random.NextDouble() < 0.1 + _smoothBass)
                    {
                        var color = GetColor(colorMode, intensity, (float)col / columns);
                        using (
                            var brush = new SolidBrush(
                                Color.FromArgb((int)(intensity * 255), color)
                            )
                        )
                        using (var font = new Font("Arial", 12))
                        {
                            g.DrawString(_random.Next(2).ToString(), font, brush, x, y);
                        }
                    }
                }
            }
        }

        public void Draw3DWaveformGrid(Graphics g, float[] samples, ColorMode colorMode)
        {
            // Update grid with new audio data
            for (int i = 49; i > 0; i--)
            {
                for (int j = 0; j < 50; j++)
                {
                    _waveformGrid[j, i] = _waveformGrid[j, i - 1];
                }
            }

            // Add new row
            for (int j = 0; j < 50; j++)
            {
                int sampleIndex = (j * samples.Length) / 50;
                _waveformGrid[j, 0] = samples[sampleIndex];
            }

            // Draw 3D projection
            float centerX = _width / 2f;
            float centerY = _height / 2f;
            float scale = 10f;

            for (int z = 0; z < 49; z++)
            {
                for (int x = 0; x < 49; x++)
                {
                    // Calculate 3D to 2D projection
                    float x3d = (x - 25) * scale;
                    float y3d = _waveformGrid[x, z] * 100;
                    float z3d = (z - 25) * scale;

                    // Simple perspective projection
                    float perspective = 1f / (1f + z3d * 0.01f);
                    float screenX1 = centerX + x3d * perspective;
                    float screenY1 = centerY - y3d * perspective + z3d * 0.5f;

                    // Next point
                    float x3d2 = ((x + 1) - 25) * scale;
                    float y3d2 = _waveformGrid[x + 1, z] * 100;
                    float screenX2 = centerX + x3d2 * perspective;
                    float screenY2 = centerY - y3d2 * perspective + z3d * 0.5f;

                    float intensity = Math.Abs(_waveformGrid[x, z]) * 2f;
                    var color = GetColor(colorMode, intensity * perspective, (float)x / 50);

                    using (var pen = new Pen(Color.FromArgb((int)(perspective * 255), color), 1))
                    {
                        g.DrawLine(pen, screenX1, screenY1, screenX2, screenY2);
                    }
                }
            }
        }

        public void DrawLaserShow(Graphics g, float[] samples, ColorMode colorMode)
        {
            // Update rotation
            _rotationAngle += _smoothBass * 0.1f;

            // Update light beams
            foreach (var beam in _lightBeams)
            {
                beam.Angle += beam.RotationSpeed + _smoothMid * 0.05f;
                beam.Length = 200 + _smoothBass * 500;
                beam.Intensity = Math.Min(1f, _smoothHigh * 2f);
            }

            // Draw beams
            float centerX = _width / 2f;
            float centerY = _height / 2f;

            foreach (var beam in _lightBeams)
            {
                float endX = centerX + (float)Math.Cos(beam.Angle) * beam.Length;
                float endY = centerY + (float)Math.Sin(beam.Angle) * beam.Length;

                // Draw beam with glow
                for (int i = 0; i < 5; i++)
                {
                    float width = (5 - i) * 2;
                    float alpha = beam.Intensity * (1f - i * 0.2f);
                    var color = GetColor(colorMode, alpha, (float)(beam.Angle / (2 * Math.PI)));

                    using (var pen = new Pen(Color.FromArgb((int)(alpha * 100), color), width))
                    {
                        g.DrawLine(pen, centerX, centerY, endX, endY);
                    }
                }
            }
        }

        public void DrawRippleField(Graphics g, float[] samples, ColorMode colorMode)
        {
            // Create new ripples on beats
            float currentAmplitude = samples.Take(100).Select(Math.Abs).Average();
            if (currentAmplitude > 0.3f && _ripples.Count < 10)
            {
                _ripples.Add(
                    new RippleEffect
                    {
                        X = _random.Next(_width),
                        Y = _random.Next(_height),
                        Radius = 0,
                        MaxRadius = 200 + currentAmplitude * 300,
                        Intensity = currentAmplitude,
                    }
                );
            }

            // Update and draw ripples
            for (int i = _ripples.Count - 1; i >= 0; i--)
            {
                var ripple = _ripples[i];
                ripple.Radius += 5;
                ripple.Intensity *= 0.95f;

                if (ripple.Radius > ripple.MaxRadius || ripple.Intensity < 0.01f)
                {
                    _ripples.RemoveAt(i);
                    continue;
                }

                // Draw ripple
                for (int r = 0; r < 3; r++)
                {
                    float radius = ripple.Radius - r * 10;
                    if (radius > 0)
                    {
                        float alpha =
                            ripple.Intensity * (1f - (float)ripple.Radius / ripple.MaxRadius);
                        var color = GetColor(colorMode, alpha, ripple.Radius / ripple.MaxRadius);

                        using (var pen = new Pen(Color.FromArgb((int)(alpha * 255), color), 2))
                        {
                            g.DrawEllipse(
                                pen,
                                ripple.X - radius,
                                ripple.Y - radius,
                                radius * 2,
                                radius * 2
                            );
                        }
                    }
                }
            }
        }

        public void DrawFractalTree(Graphics g, float[] samples, ColorMode colorMode)
        {
            float centerX = _width / 2f;
            float baseY = _height - 100;
            float trunkHeight = 200 + _smoothBass * 100;

            DrawBranch(
                g,
                centerX,
                baseY,
                centerX,
                baseY - trunkHeight,
                10,
                0,
                samples,
                colorMode,
                0
            );
        }

        private void DrawBranch(
            Graphics g,
            float x1,
            float y1,
            float x2,
            float y2,
            int depth,
            float angle,
            float[] samples,
            ColorMode colorMode,
            int sampleOffset
        )
        {
            if (depth <= 0)
                return;

            // Draw current branch
            float intensity = Math.Min(1f, samples[sampleOffset % samples.Length] * 2f + 0.5f);
            var color = GetColor(colorMode, intensity, (float)depth / 10);

            using (var pen = new Pen(color, Math.Max(1, depth / 2)))
            {
                g.DrawLine(pen, x1, y1, x2, y2);
            }

            // Calculate branch splits
            float branchLength =
                (float)Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2)) * 0.7f;
            float audioInfluence = samples[(sampleOffset + depth * 10) % samples.Length];
            float angleVariation = 0.5f + audioInfluence;

            // Left branch
            float leftAngle = angle - angleVariation;
            float leftX = x2 + (float)Math.Sin(leftAngle) * branchLength;
            float leftY = y2 - (float)Math.Cos(leftAngle) * branchLength;
            DrawBranch(
                g,
                x2,
                y2,
                leftX,
                leftY,
                depth - 1,
                leftAngle,
                samples,
                colorMode,
                sampleOffset + 1
            );

            // Right branch
            float rightAngle = angle + angleVariation;
            float rightX = x2 + (float)Math.Sin(rightAngle) * branchLength;
            float rightY = y2 - (float)Math.Cos(rightAngle) * branchLength;
            DrawBranch(
                g,
                x2,
                y2,
                rightX,
                rightY,
                depth - 1,
                rightAngle,
                samples,
                colorMode,
                sampleOffset + 2
            );
        }

        private void InitializeLightBeams()
        {
            for (int i = 0; i < 8; i++)
            {
                _lightBeams.Add(
                    new LightBeam
                    {
                        Angle = (float)(i * Math.PI / 4),
                        Length = 200,
                        RotationSpeed = (_random.NextSingle() - 0.5f) * 0.02f,
                        Intensity = 1f,
                    }
                );
            }
        }

        private class LightBeam
        {
            public float Angle { get; set; }
            public float Length { get; set; }
            public float RotationSpeed { get; set; }
            public float Intensity { get; set; }
        }

        private class RippleEffect
        {
            public float X { get; set; }
            public float Y { get; set; }
            public float Radius { get; set; }
            public float MaxRadius { get; set; }
            public float Intensity { get; set; }
        }
    }
}
