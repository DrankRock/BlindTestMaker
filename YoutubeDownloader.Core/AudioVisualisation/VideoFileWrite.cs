using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YoutubeDownloader.Core.AudioVisualisation
{
    // Actual video encoding implementation using FFMpegCore
    public class VideoFileWriter : IDisposable
    {
        private Process _ffmpegProcess;
        private Stream _ffmpegInput;
        private int _width;
        private int _height;
        private int _fps;

        public void Open(string path, int width, int height, int fps, VideoCodec codec)
        {
            Debug.WriteLine(
                $"VideoFileWriter.Open - Path: {path}, Width: {width}, Height: {height}, FPS: {fps}, Codec: {codec}"
            );
            _width = width;
            _height = height;
            _fps = fps;

            string codecString = codec switch
            {
                VideoCodec.H264 => "libx264",
                VideoCodec.H265 => "libx265",
                VideoCodec.VP9 => "libvpx-vp9",
                _ => "libx264",
            };
            Debug.WriteLine($"VideoFileWriter.Open - Selected FFMpeg codec string: {codecString}");

            var arguments =
                $"-y -f rawvideo -pix_fmt bgr24 -s {width}x{height} -r {fps} -i - "
                + $"-c:v {codecString} -preset ultrafast -crf 22 -pix_fmt yuv420p \"{path}\"";
            // Changed preset to ultrafast and CRF to 22 for potentially faster encoding during debugging, can be reverted.

            Debug.WriteLine($"VideoFileWriter.Open - FFMpeg arguments: {arguments}");

            var startInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true, // Capture output for debugging
                RedirectStandardError = true, // Capture errors for debugging
                CreateNoWindow = true,
            };

            Debug.WriteLine("VideoFileWriter.Open - Starting FFMpeg process...");
            _ffmpegProcess = new Process { StartInfo = startInfo };

            _ffmpegProcess.OutputDataReceived += (sender, args) =>
                Debug.WriteLine($"FFmpeg (VideoWrite) Output: {args.Data}");
            _ffmpegProcess.ErrorDataReceived += (sender, args) =>
                Debug.WriteLine($"FFmpeg (VideoWrite) Error: {args.Data}");

            _ffmpegProcess.Start();
            _ffmpegProcess.BeginOutputReadLine();
            _ffmpegProcess.BeginErrorReadLine();

            _ffmpegInput = _ffmpegProcess.StandardInput.BaseStream;
            Debug.WriteLine(
                "VideoFileWriter.Open - FFMpeg process started and input stream acquired."
            );
        }

        public void WriteVideoFrame(Bitmap frame)
        {
            // This can be very verbose, so log only periodically or key info
            // Debug.WriteLine($"VideoFileWriter.WriteVideoFrame - Writing frame. Dimensions: {frame.Width}x{frame.Height}");
            if (
                _ffmpegProcess == null
                || _ffmpegProcess.HasExited
                || _ffmpegInput == null
                || !_ffmpegInput.CanWrite
            )
            {
                Debug.WriteLine(
                    "VideoFileWriter.WriteVideoFrame - FFMpeg process is not running or stream is not writable. Skipping frame write."
                );
                return;
            }

            var rect = new Rectangle(0, 0, _width, _height);
            BitmapData bmpData = null;
            try
            {
                bmpData = frame.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                // Debug.WriteLine($"VideoFileWriter.WriteVideoFrame - Frame locked. Stride: {bmpData.Stride}");

                int bytes = Math.Abs(bmpData.Stride) * frame.Height;
                byte[] rgbValues = new byte[bytes];
                System.Runtime.InteropServices.Marshal.Copy(bmpData.Scan0, rgbValues, 0, bytes);
                // Debug.WriteLine($"VideoFileWriter.WriteVideoFrame - Copied {bytes} bytes from bitmap to byte array.");

                _ffmpegInput.Write(rgbValues, 0, rgbValues.Length);
                _ffmpegInput.Flush(); // Important to flush the stream
                // Debug.WriteLine($"VideoFileWriter.WriteVideoFrame - {bytes} bytes written to FFMpeg input stream and flushed.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"VideoFileWriter.WriteVideoFrame - Exception during frame write: {ex.Message}"
                );
                // Consider how to handle this - re-throw, stop, etc.
            }
            finally
            {
                if (bmpData != null)
                {
                    frame.UnlockBits(bmpData);
                    // Debug.WriteLine("VideoFileWriter.WriteVideoFrame - Frame unlocked.");
                }
            }
        }

        public void Close()
        {
            Debug.WriteLine("VideoFileWriter.Close - Closing FFMpeg process and stream.");
            try
            {
                if (_ffmpegInput != null)
                {
                    Debug.WriteLine("VideoFileWriter.Close - Closing FFMpeg input stream.");
                    _ffmpegInput.Flush();
                    _ffmpegInput.Close();
                    _ffmpegInput = null;
                    Debug.WriteLine("VideoFileWriter.Close - FFMpeg input stream closed.");
                }

                if (_ffmpegProcess != null && !_ffmpegProcess.HasExited)
                {
                    Debug.WriteLine(
                        "VideoFileWriter.Close - Waiting for FFMpeg process to exit..."
                    );
                    if (_ffmpegProcess.WaitForExit(5000)) // Wait for 5 seconds
                    {
                        Debug.WriteLine(
                            $"VideoFileWriter.Close - FFMpeg process exited gracefully with code: {_ffmpegProcess.ExitCode}."
                        );
                    }
                    else
                    {
                        Debug.WriteLine(
                            "VideoFileWriter.Close - FFMpeg process did not exit in time, killing."
                        );
                        _ffmpegProcess.Kill();
                        Debug.WriteLine("VideoFileWriter.Close - FFMpeg process killed.");
                    }
                }
                else if (_ffmpegProcess != null)
                {
                    Debug.WriteLine(
                        $"VideoFileWriter.Close - FFMpeg process had already exited with code: {_ffmpegProcess.ExitCode}."
                    );
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"VideoFileWriter.Close - Exception during close: {ex.Message}");
            }
            finally
            {
                if (_ffmpegProcess != null)
                {
                    _ffmpegProcess.Dispose();
                    _ffmpegProcess = null;
                    Debug.WriteLine("VideoFileWriter.Close - FFMpeg process disposed.");
                }
            }
            Debug.WriteLine("VideoFileWriter.Close - Close method finished.");
        }

        public void Dispose()
        {
            Debug.WriteLine("VideoFileWriter.Dispose - Dispose called.");
            Close();
            GC.SuppressFinalize(this); // Prevent finalizer from running if Dispose is called.
            Debug.WriteLine("VideoFileWriter.Dispose - Dispose finished.");
        }

        ~VideoFileWriter()
        {
            Debug.WriteLine(
                "VideoFileWriter.~VideoFileWriter - Finalizer called. This indicates Dispose() was not called. Closing resources."
            );
            Close(); // Ensure resources are released if Dispose was not called.
        }
    }
}
