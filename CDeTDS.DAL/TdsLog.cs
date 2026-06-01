using System;
using System.IO;

namespace CDeTDS.DAL
{
    public static class TdsLog
    {
        public static readonly string LogPath = Path.Combine(Path.GetTempPath(), "tds_debug.log");
        private static readonly object _lock = new();

        public static void Write(string msg)
        {
            try
            {
                lock (_lock)
                {
                    File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {msg}{Environment.NewLine}");
                }
            }
            catch { }
        }

        public static void Clear()
        {
            try { File.WriteAllText(LogPath, $"=== TDS Debug Log started {DateTime.Now} ==={Environment.NewLine}"); } catch { }
        }
    }
}
