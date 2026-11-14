using System;
using System.IO;

namespace Nook
{
    internal static class Log
    {
        private static readonly string Path =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nook.log");

        public static void Write(string message)
        {
            try
            {
                File.AppendAllText(Path, $"{DateTime.Now:HH:mm:ss.fff}  {message}\r\n");
            }
            catch { }
        }
    }
}
