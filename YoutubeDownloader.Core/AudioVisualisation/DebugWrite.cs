using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls.Shapes;

namespace YoutubeDownloader.Core.AudioVisualisation
{
    public static class DebugWrite
    {
        private static bool _DEBUG = false;

        public static void Line(string s)
        {
            if (_DEBUG)
            {
                Debug.WriteLine(s);
            }
        }
    }
}
