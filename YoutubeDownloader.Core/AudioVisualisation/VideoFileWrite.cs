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

            var startInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments =
                    $"-y -f rawvideo -pix_fmt bgr24 -s {width}x{height} -r {fps} -i - "
                    + $"-c:v {codecString} -preset fast -crf 18 -pix_fmt yuv420p \"{path}\"",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            _ffmpegProcess = Process.Start(startInfo);
            _ffmpegInput = _ffmpegProcess.StandardInput.BaseStream;
        }

        public void WriteVideoFrame(Bitmap frame)
        {
            var rect = new Rectangle(0, 0, _width, _height);
            var bmpData = frame.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

            try
            {
                int bytes = Math.Abs(bmpData.Stride) * frame.Height;
                byte[] rgbValues = new byte[bytes];
                System.Runtime.InteropServices.Marshal.Copy(bmpData.Scan0, rgbValues, 0, bytes);

                _ffmpegInput.Write(rgbValues, 0, rgbValues.Length);
                _ffmpegInput.Flush();
            }
            finally
            {
                frame.UnlockBits(bmpData);
            }
        }

        public void Close()
        {
            _ffmpegInput?.Close();
            _ffmpegProcess?.WaitForExit();
            _ffmpegProcess?.Dispose();
        }

        public void Dispose()
        {
            Close();
        }
    }
}
