using System.IO;

namespace YiMengFix.UI;

public sealed class FileLogger
{
    private readonly object _sync = new();

    public FileLogger()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YiMengFix",
            "logs");
        Directory.CreateDirectory(directory);
        LogPath = Path.Combine(directory, $"{DateTime.Now:yyyyMMdd-HHmmss}.log");
        Write($"YiMengFix {typeof(FileLogger).Assembly.GetName().Version}");
    }

    public string LogPath { get; }

    public void Write(string message)
    {
        lock (_sync)
        {
            File.AppendAllText(LogPath, message + Environment.NewLine);
        }
    }
}
