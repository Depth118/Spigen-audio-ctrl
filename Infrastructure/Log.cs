using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace SpigenAudioCTRL.Infrastructure
{
    // Small rolling file log at %LOCALAPPDATA%\SpigenAudioCTRL\logs\app.log, for bug reports.
    public static class Log
    {
        private const long MaxBytes = 1024 * 1024;
        private static readonly object Gate = new();

        public static string Directory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpigenAudioCTRL", "logs");

        public static string FilePath { get; } = Path.Combine(Directory, "app.log");

        public static void Info(string category, string message) => Write("INFO", category, message);

        public static void Warn(string category, string message) => Write("WARN", category, message);

        public static void Error(string category, string message, Exception? ex = null) =>
            Write("ERROR", category, ex == null ? message : $"{message}: {ex}");

        // Returns the current log (and the previous one, if rotated) for pasting into an issue.
        public static string ReadAll()
        {
            lock (Gate)
            {
                var text = new StringBuilder();
                foreach (var path in new[] { FilePath + ".1", FilePath })
                {
                    try
                    {
                        if (File.Exists(path)) text.Append(File.ReadAllText(path));
                    }
                    catch (IOException) { }
                }
                return text.ToString();
            }
        }

        private static void Write(string level, string category, string message)
        {
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level,-5} [{category}] {message}";
            Debug.WriteLine(line);

            lock (Gate)
            {
                try
                {
                    System.IO.Directory.CreateDirectory(Directory);
                    var info = new FileInfo(FilePath);
                    if (info.Exists && info.Length > MaxBytes)
                    {
                        File.Move(FilePath, FilePath + ".1", overwrite: true);
                    }
                    File.AppendAllText(FilePath, line + Environment.NewLine);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Logging must never take the app down.
                }
            }
        }
    }
}
