using System.Text.Json;
using System.IO;

namespace YiMengFix.UI;

public sealed class AppSettings
{
    public string? LastGameRoot { get; set; }
    public bool DarkMode { get; set; }
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _settingsPath;

    public SettingsStore()
    {
        _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "YiMengFix",
            "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            return File.Exists(_settingsPath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath)) ?? new AppSettings()
                : new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        var temporary = $"{_settingsPath}.tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporary, _settingsPath, overwrite: true);
    }
}
