using System.Diagnostics;

namespace YiMengFix.Core;

public sealed class WindowsProcessDetector : IProcessDetector
{
    public bool IsGameRunning(string gameRoot)
    {
        var root = Path.GetFullPath(gameRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        foreach (var name in FixConstants.ProcessNames)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    try
                    {
                        var processPath = process.MainModule?.FileName;
                        if (processPath is not null &&
                            Path.GetFullPath(processPath).StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                    catch
                    {
                        // Protected processes are ignored unless their executable path can be verified.
                    }
                }
            }
        }

        return false;
    }
}
