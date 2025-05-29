using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            Debug.WriteLine(
                $"AdvancedVisualizer.Constructor - Initializing with Width: {width}, Height: {height}, FPS: {fps}, OutputPath: {outputPath}"
            );
            _waveformGrid = new float[50, 50];
            Debug.WriteLine(
                $"AdvancedVisualizer.Constructor - WaveformGrid initialized with dimensions 50x50."
            );
            InitializeLightBeams();
            Debug.WriteLine(
                "AdvancedVisualizer.Constructor - AdvancedVisualizer initialization complete."
            );
        }

        public void DrawMatrixRain(Graphics g, float[] samples, ColorMode colorMode)
        {
            Debug.WriteLine(
                $"AdvancedVisualizer.DrawMatrixRain - Drawing with {samples.Length} samples. Frame: {_frameCount}"
            );
            if (samples.Length == 0)
            {
                Debug.WriteLine("AdvancedVisualizer.DrawMatrixRain - No samples to draw.");
                return;
            }

            int columns = _width / 20;
            Debug.WriteLine($"AdvancedVisualizer.DrawMatrixRain - Columns: {columns}");

            for (int col = 0; col < columns; col++)
            {
                float x = col * 20;
                // Ensure sample index is within bounds
                int sampleIndex = col % samples.Length;
                float audioInfluence = samples[sampleIndex] * 10;
                // if (col == 0) Debug.WriteLine($"AdvancedVisualizer.DrawMatrixRain - Col 0: X={x}, SampleVal={samples[sampleIndex]}, AudioInfluence={audioInfluence}");


                for (int row = 0; row < _height / 20; row++)
                {
                    // Calculate Y position with wrap-around effect for continuous rain
                    float y = (row * 20 + (_frameCount * (2 + _smoothBass * 5))) % _height; // Modulo height for wrap
                    float intensity = Math.Max(
                        0.1f,
                        Math.Min(
                            1f,
                            (float)row / (_height / 20f) * (0.5f + Math.Abs(audioInfluence))
                                + _smoothHigh * 0.3f
                        )
                    ); // More dynamic intensity

                    if (_random.NextDouble() < 0.05 + _smoothBass * 0.2) // Adjust spawn probability
                    {
                        var color = GetColor(colorMode, intensity, (float)col / columns);
                        using (
                            var brush = new SolidBrush(
                                Color.FromArgb(
                                    Math.Max(0, Math.Min(255, (int)(intensity * 200 + 55))),
                                    color
                                )
                            )
                        ) // Ensure alpha is not too low
                        using (var font = new Font("Consolas", 14, FontStyle.Bold)) // Monospaced font often looks better
                        {
                            char randomChar = Convert.ToChar(_random.Next(33, 126)); // Printable ASCII
                            g.DrawString(randomChar.ToString(), font, brush, x, y);
                        }
                    }
                }
            }
            Debug.WriteLine("AdvancedVisualizer.DrawMatrixRain - Finished drawing matrix rain.");
        }

        public void Draw3DWaveformGrid(Graphics g, float[] samples, ColorMode colorMode)
        {
            Debug.WriteLine(
                $"AdvancedVisualizer.Draw3DWaveformGrid - Drawing with {samples.Length} samples."
            );
            if (samples.Length == 0)
            {
                Debug.WriteLine(
                    "AdvancedVisualizer.Draw3DWaveformGrid - No samples for grid update."
                );
                return;
            }

            Debug.WriteLine(
                "AdvancedVisualizer.Draw3DWaveformGrid - Updating grid with new audio data."
            );
            // Shift existing data back
            for (int z = 49; z > 0; z--) // Grid depth (z-axis)
            {
                for (int x = 0; x < 50; x++) // Grid width (x-axis)
                {
                    _waveformGrid[x, z] = _waveformGrid[x, z - 1];
                }
            }

            // Add new row of data from current samples
            Debug.WriteLine("AdvancedVisualizer.Draw3DWaveformGrid - Adding new row to grid.");
            for (int x = 0; x < 50; x++)
            {
                int sampleIndex = (x * samples.Length) / 50; // Map grid column to sample index
                _waveformGrid[x, 0] = samples[sampleIndex];
                if (x == 0)
                    Debug.WriteLine(
                        $"AdvancedVisualizer.Draw3DWaveformGrid - Grid[0,0] set to sample[{sampleIndex}] = {samples[sampleIndex]}"
                    );
            }

            float centerX = _width / 2f;
            float centerY = _height * 0.6f; // Adjust vertical center for better perspective
            float baseScale = Math.Min(_width, _height) / 60f; // Scale relative to screen size
            float amplitudeScale = _height / 10f; // Scale for waveform height
            float depthFactor = 0.005f; // Controls perspective intensity

            Debug.WriteLine(
                $"AdvancedVisualizer.Draw3DWaveformGrid - Drawing 3D projection. Center: ({centerX},{centerY}), BaseScale: {baseScale}"
            );

            for (int z = 0; z < 49; z++) // Iterate through depth
            {
                for (int x = 0; x < 49; x++) // Iterate through width
                {
                    // Points for the current line segment (from (x,z) to (x+1,z))
                    float x3d_1 = (x - 24.5f) * baseScale; // Center the grid
                    float y3d_1 = _waveformGrid[x, z] * amplitudeScale;
                    float z3d_1 = (z - 24.5f) * baseScale;

                    float x3d_2 = ((x + 1) - 24.5f) * baseScale;
                    float y3d_2 = _waveformGrid[x + 1, z] * amplitudeScale;
                    // z3d_2 is same as z3d_1 for this segment along x


                    // Simple perspective projection
                    float perspective1 = 1f / (1f + z3d_1 * depthFactor + _smoothBass * 0.1f); // Bass affects perspective
                    float screenX1 = centerX + x3d_1 * perspective1;
                    float screenY1 = centerY - y3d_1 * perspective1 + z3d_1 * 0.3f * perspective1; // Tilt effect

                    float perspective2 = 1f / (1f + z3d_1 * depthFactor + _smoothBass * 0.1f); // Use same perspective for points on same z-depth line
                    float screenX2 = centerX + x3d_2 * perspective2;
                    float screenY2 = centerY - y3d_2 * perspective2 + z3d_1 * 0.3f * perspective2;

                    float intensity = Math.Abs(_waveformGrid[x, z]) * 2f + _smoothHigh * 0.5f; // Highs add sparkle
                    var color = GetColor(
                        colorMode,
                        intensity * perspective1,
                        (float)x / 50 + (float)z / 100
                    );

                    if (x == 0 && z == 0)
                        Debug.WriteLine(
                            $"AdvancedVisualizer.Draw3DWaveformGrid - First segment: ({screenX1},{screenY1}) to ({screenX2},{screenY2}), Persp1={perspective1}"
                        );

                    using (
                        var pen = new Pen(
                            Color.FromArgb(
                                Math.Max(0, Math.Min(255, (int)(perspective1 * 200 + 55))),
                                color
                            ),
                            Math.Max(1, 2 * perspective1)
                        )
                    )
                    {
                        g.DrawLine(pen, screenX1, screenY1, screenX2, screenY2);
                    }

                    // Optionally, draw lines along Z axis as well (connecting (x,z) to (x,z+1))
                    if (z < 48)
                    {
                        float y3d_next_z = _waveformGrid[x, z + 1] * amplitudeScale;
                        float z3d_next_z = ((z + 1) - 24.5f) * baseScale;
                        float perspective_next_z =
                            1f / (1f + z3d_next_z * depthFactor + _smoothBass * 0.1f);
                        float screenY_next_z =
                            centerY
                            - y3d_next_z * perspective_next_z
                            + z3d_next_z * 0.3f * perspective_next_z;
                        float screenX_next_z = centerX + x3d_1 * perspective_next_z; // x is same

                        using (
                            var pen = new Pen(
                                Color.FromArgb(
                                    Math.Max(0, Math.Min(255, (int)(perspective1 * 180 + 55))),
                                    color
                                ),
                                Math.Max(1, 1.5f * perspective1)
                            )
                        )
                        {
                            g.DrawLine(pen, screenX1, screenY1, screenX_next_z, screenY_next_z);
                        }
                    }
                }
            }
            Debug.WriteLine("AdvancedVisualizer.Draw3DWaveformGrid - Finished drawing grid.");
        }

        public void DrawLaserShow(Graphics g, float[] samples, ColorMode colorMode)
        {
            Debug.WriteLine(
                $"AdvancedVisualizer.DrawLaserShow - Drawing with {samples.Length} samples. Beam count: {_lightBeams.Count}"
            );

            _rotationAngle += _smoothBass * 0.05f + 0.001f; // Slow base rotation + bass influence
            if (_rotationAngle > Math.PI * 2)
                _rotationAngle -= (float)(Math.PI * 2);
            // Debug.WriteLine($"AdvancedVisualizer.DrawLaserShow - Global RotationAngle: {_rotationAngle}");


            foreach (var beam in _lightBeams)
            {
                beam.Angle += beam.RotationSpeed + _smoothMid * 0.02f; // Individual rotation + mid influence
                if (beam.Angle > Math.PI * 2)
                    beam.Angle -= (float)(Math.PI * 2);
                if (beam.Angle < 0)
                    beam.Angle += (float)(Math.PI * 2);

                beam.Length = Math.Max(
                    100,
                    200
                        + _smoothBass * _width * 0.3f
                        + Math.Abs(samples.Length > 0 ? samples[0] : 0) * _width * 0.2f
                ); // Bass and direct sample influence length
                beam.Length = Math.Min(beam.Length, Math.Max(_width, _height) * 0.75f); // Cap length

                beam.Intensity = Math.Max(
                    0.1f,
                    Math.Min(1f, _smoothHigh * 1.5f + _smoothMid * 0.5f)
                ); // Highs and mids control intensity
                // if (_lightBeams.First() == beam) Debug.WriteLine($"AdvancedVisualizer.DrawLaserShow - Beam 0: Angle={beam.Angle}, Length={beam.Length}, Intensity={beam.Intensity}");
            }

            float centerX = _width / 2f;
            float centerY = _height / 2f;

            foreach (var beam in _lightBeams)
            {
                // Apply global rotation
                float effectiveAngle = beam.Angle + _rotationAngle;

                float endX = centerX + (float)Math.Cos(effectiveAngle) * beam.Length;
                float endY = centerY + (float)Math.Sin(effectiveAngle) * beam.Length;

                for (int i = 0; i < 5; i++) // Layers for glow
                {
                    float width = (5 - i) * (2 + _smoothBass * 2); // Width affected by bass
                    float alpha = beam.Intensity * (1f - i * 0.18f); // Slightly adjusted alpha falloff for layers
                    var color = GetColor(colorMode, alpha, (float)(effectiveAngle / (2 * Math.PI)));

                    using (
                        var pen = new Pen(
                            Color.FromArgb(Math.Max(0, Math.Min(255, (int)(alpha * 150))), color),
                            Math.Max(1, width)
                        )
                    ) // Alpha for glow layers is lower
                    {
                        g.DrawLine(pen, centerX, centerY, endX, endY);
                    }
                }
                // Main bright beam
                var mainColor = GetColor(
                    colorMode,
                    beam.Intensity,
                    (float)(effectiveAngle / (2 * Math.PI))
                );
                using (
                    var pen = new Pen(
                        Color.FromArgb(
                            Math.Max(0, Math.Min(255, (int)(beam.Intensity * 255))),
                            mainColor
                        ),
                        Math.Max(1, 2 + _smoothBass)
                    )
                )
                {
                    g.DrawLine(pen, centerX, centerY, endX, endY);
                }
            }
            Debug.WriteLine("AdvancedVisualizer.DrawLaserShow - Finished drawing lasers.");
        }

        public void DrawRippleField(Graphics g, float[] samples, ColorMode colorMode)
        {
            Debug.WriteLine(
                $"AdvancedVisualizer.DrawRippleField - Drawing with {samples.Length} samples. Ripple count: {_ripples.Count}"
            );
            if (samples.Length == 0)
            {
                Debug.WriteLine(
                    "AdvancedVisualizer.DrawRippleField - No samples for ripple logic."
                );
            }

            float currentAmplitude = 0;
            if (samples.Length > 0)
            {
                currentAmplitude = samples
                    .Take(Math.Min(samples.Length, 100))
                    .Select(Math.Abs)
                    .Average();
            }
            Debug.WriteLine(
                $"AdvancedVisualizer.DrawRippleField - CurrentAmplitude: {currentAmplitude}"
            );

            // Create new ripples on beats (more sensitive threshold, bass dependent)
            if (
                (currentAmplitude > 0.15f + _smoothBass * 0.1f || _smoothBass > 0.5f)
                && _ripples.Count < 15
                && _random.NextDouble() < 0.2 + _smoothBass * 0.5
            )
            {
                Debug.WriteLine("AdvancedVisualizer.DrawRippleField - Spawning new ripple.");
                var newRipple = new RippleEffect
                {
                    X = _random.Next(_width),
                    Y = _random.Next(_height),
                    Radius = 0,
                    MaxRadius =
                        100 + currentAmplitude * _width * 0.2f + _smoothBass * _width * 0.3f,
                    Intensity = currentAmplitude * 0.5f + _smoothBass * 0.5f, // Mix of direct amplitude and bass
                    Speed = 3 + _smoothMid * 5f, // Mid frequencies affect ripple speed
                };
                _ripples.Add(newRipple);
                Debug.WriteLine(
                    $"AdvancedVisualizer.DrawRippleField - New Ripple: X={newRipple.X}, Y={newRipple.Y}, MaxR={newRipple.MaxRadius}, Intensity={newRipple.Intensity}, Speed={newRipple.Speed}"
                );
            }

            for (int i = _ripples.Count - 1; i >= 0; i--)
            {
                var ripple = _ripples[i];
                ripple.Radius += ripple.Speed;
                ripple.Intensity *= (0.92f - _smoothHigh * 0.05f); // High frequencies make ripples fade faster

                if (ripple.Radius > ripple.MaxRadius || ripple.Intensity < 0.005f)
                {
                    _ripples.RemoveAt(i);
                    // Debug.WriteLine($"AdvancedVisualizer.DrawRippleField - Ripple removed. Radius={ripple.Radius}, Intensity={ripple.Intensity}");
                    continue;
                }

                // Draw multiple concentric circles for each ripple for a better effect
                for (int rLayer = 0; rLayer < 3; rLayer++)
                {
                    float currentLayerRadius = ripple.Radius - rLayer * (15 + _smoothMid * 20f); // Space out layers
                    if (currentLayerRadius > 0)
                    {
                        // Alpha decreases with radius and layer index
                        float alpha =
                            ripple.Intensity
                            * (1f - (float)currentLayerRadius / ripple.MaxRadius)
                            * (1f - rLayer * 0.2f);
                        alpha = Math.Max(0, Math.Min(1, alpha));

                        var color = GetColor(
                            colorMode,
                            alpha,
                            ripple.X / _width + ripple.Y / _height
                        ); // Position influences color slightly

                        using (
                            var pen = new Pen(
                                Color.FromArgb(
                                    Math.Max(0, Math.Min(255, (int)(alpha * 255))),
                                    color
                                ),
                                Math.Max(1, 3 - rLayer)
                            )
                        ) // Thinner outer layers
                        {
                            g.DrawEllipse(
                                pen,
                                ripple.X - currentLayerRadius,
                                ripple.Y - currentLayerRadius,
                                currentLayerRadius * 2,
                                currentLayerRadius * 2
                            );
                        }
                    }
                }
                // if (i==0) Debug.WriteLine($"AdvancedVisualizer.DrawRippleField - Ripple 0 updated: Radius={ripple.Radius}, Intensity={ripple.Intensity}");
            }
            Debug.WriteLine("AdvancedVisualizer.DrawRippleField - Finished drawing ripples.");
        }

        public void DrawFractalTree(Graphics g, float[] samples, ColorMode colorMode)
        {
            Debug.WriteLine(
                $"AdvancedVisualizer.DrawFractalTree - Drawing with {samples.Length} samples."
            );
            if (samples.Length == 0)
            {
                Debug.WriteLine("AdvancedVisualizer.DrawFractalTree - No samples to draw tree.");
                return;
            }

            float centerX = _width / 2f;
            float baseY = _height - 50; // Start tree a bit higher
            float trunkHeight = Math.Max(50, _height / 6f + _smoothBass * (_height / 5f)); // Bass influences trunk height
            trunkHeight = Math.Min(trunkHeight, _height * 0.4f); // Cap trunk height

            int initialDepth = 7 + (int)(_smoothMid * 3); // Mid frequencies influence complexity/depth
            initialDepth = Math.Min(initialDepth, 10); // Cap depth
            initialDepth = Math.Max(5, initialDepth);

            Debug.WriteLine(
                $"AdvancedVisualizer.DrawFractalTree - CenterX: {centerX}, BaseY: {baseY}, TrunkHeight: {trunkHeight}, InitialDepth: {initialDepth}"
            );

            DrawBranch(
                g,
                centerX,
                baseY,
                centerX,
                baseY - trunkHeight,
                initialDepth,
                0,
                samples,
                colorMode,
                0
            );
            Debug.WriteLine("AdvancedVisualizer.DrawFractalTree - Finished drawing fractal tree.");
        }

        private void DrawBranch(
            Graphics g,
            float x1,
            float y1, // Start point
            float x2,
            float y2, // End point
            int depth,
            float angle, // Angle of the current branch (radians from vertical)
            float[] samples,
            ColorMode colorMode,
            int sampleOffset // To vary audio influence along branches
        )
        {
            // Debug.WriteLine($"AdvancedVisualizer.DrawBranch - Depth: {depth}, Angle: {angle}, Start: ({x1},{y1}), End: ({x2},{y2}), SampleOffset: {sampleOffset}");
            if (depth <= 0 || y2 < 0 || y2 > _height || x2 < 0 || x2 > _width) // Stop if too deep or off-screen
            {
                // Debug.WriteLine("AdvancedVisualizer.DrawBranch - Branch limit reached (depth or off-screen).");
                return;
            }

            // Ensure sampleOffset is within bounds of samples array
            int currentSampleIndex = Math.Max(
                0,
                Math.Min(samples.Length - 1, (sampleOffset + depth * 5) % samples.Length)
            );
            float audioSampleValue = samples[currentSampleIndex];

            float intensity = Math.Max(
                0.1f,
                Math.Min(1f, Math.Abs(audioSampleValue) * 1.5f + 0.3f + _smoothHigh * 0.3f)
            );
            var color = GetColor(
                colorMode,
                intensity,
                (float)depth / 10f + (angle / (float)(Math.PI * 2f))
            ); // Color varies with depth and angle

            float branchWidth = Math.Max(1, depth * (0.5f + _smoothBass * 0.2f)); // Bass makes branches thicker
            using (var pen = new Pen(color, branchWidth))
            {
                g.DrawLine(pen, x1, y1, x2, y2);
            }

            // Calculate length of the current branch to scale new branches
            float currentBranchLength = (float)
                Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));
            float newBranchLength =
                currentBranchLength
                * (0.6f + Math.Abs(audioSampleValue) * 0.2f + _smoothMid * 0.1f); // Audio and mid influence new length
            newBranchLength = Math.Max(5, newBranchLength); // Minimum branch length

            // Audio influence on branching angle and number of branches
            float angleVariationBase = 0.3f + _smoothHigh * 0.3f; // Highs make branching wider
            float audioAngleInfluence = audioSampleValue * 0.5f; // Sample value directly influences angle asymmetry

            // Left branch
            float leftAngle = angle - (angleVariationBase - audioAngleInfluence);
            float leftX = x2 + (float)Math.Sin(leftAngle) * newBranchLength;
            float leftY = y2 - (float)Math.Cos(leftAngle) * newBranchLength; // Y grows upwards
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
                sampleOffset + depth * 2 + 1
            );

            // Right branch
            float rightAngle = angle + (angleVariationBase + audioAngleInfluence);
            float rightX = x2 + (float)Math.Sin(rightAngle) * newBranchLength;
            float rightY = y2 - (float)Math.Cos(rightAngle) * newBranchLength;
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
                sampleOffset + depth * 2 + 2
            );

            // Optional third, more central branch if bass is high
            if (_smoothBass > 0.6f && depth > 2 && _random.NextDouble() < 0.3)
            {
                float centerAngle = angle + audioSampleValue * 0.2f; // Slightly offset by audio
                float centerX = x2 + (float)Math.Sin(centerAngle) * newBranchLength * 0.8f; // Shorter
                float centerY = y2 - (float)Math.Cos(centerAngle) * newBranchLength * 0.8f;
                DrawBranch(
                    g,
                    x2,
                    y2,
                    centerX,
                    centerY,
                    depth - 2,
                    centerAngle,
                    samples,
                    colorMode,
                    sampleOffset + depth * 2 + 3
                ); // Deeper recursion faster
            }
        }

        private void InitializeLightBeams()
        {
            _lightBeams.Clear();
            int beamCount = 6 + _random.Next(0, 5); // Randomize beam count slightly
            Debug.WriteLine(
                $"AdvancedVisualizer.InitializeLightBeams - Initializing {beamCount} light beams."
            );
            for (int i = 0; i < beamCount; i++)
            {
                var beam = new LightBeam
                {
                    Angle = (float)(i * Math.PI * 2.0 / beamCount), // Distribute beams evenly
                    Length = 200 + _random.NextSingle() * 100,
                    RotationSpeed =
                        (_random.NextSingle() - 0.5f) * 0.01f
                        + 0.005f * Math.Sign(_random.NextSingle() - 0.5f), // Ensure some base speed
                    Intensity = 0.5f + _random.NextSingle() * 0.5f,
                };
                _lightBeams.Add(beam);
                Debug.WriteLine(
                    $"AdvancedVisualizer.InitializeLightBeams - Beam {i}: Angle={beam.Angle}, Length={beam.Length}, Speed={beam.RotationSpeed}, Intensity={beam.Intensity}"
                );
            }
            Debug.WriteLine(
                $"AdvancedVisualizer.InitializeLightBeams - {_lightBeams.Count} light beams initialized."
            );
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
            public float Speed { get; set; } = 5f; // Added speed property
        }
    }
}
