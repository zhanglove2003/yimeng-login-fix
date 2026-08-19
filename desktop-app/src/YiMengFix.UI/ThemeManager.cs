using System.Windows;
using System.Windows.Media;

namespace YiMengFix.UI;

public static class ThemeManager
{
    public static void Apply(bool darkMode)
    {
        var resources = Application.Current.Resources;
        Application.Current.ThemeMode = SystemParameters.HighContrast
            ? ThemeMode.System
            : darkMode ? ThemeMode.Dark : ThemeMode.Light;

        if (SystemParameters.HighContrast)
        {
            resources["WindowBackgroundBrush"] = SystemColors.WindowBrush;
            resources["TitleBarBrush"] = SystemColors.WindowBrush;
            resources["SurfaceBrush"] = SystemColors.ControlBrush;
            resources["PrimaryTextBrush"] = SystemColors.WindowTextBrush;
            resources["SecondaryTextBrush"] = SystemColors.GrayTextBrush;
            resources["AccentBrush"] = SystemColors.HighlightBrush;
            resources["AccentHoverBrush"] = SystemColors.HotTrackBrush;
            resources["AccentTextBrush"] = SystemColors.HighlightTextBrush;
            resources["BorderBrush"] = SystemColors.ActiveBorderBrush;
            resources["SubtleBrush"] = SystemColors.ControlBrush;
            resources["SubtleHoverBrush"] = SystemColors.ControlLightBrush;
            resources["SuccessBrush"] = SystemColors.HighlightBrush;
            resources["WarningBrush"] = SystemColors.HighlightBrush;
            resources["ErrorBrush"] = SystemColors.HighlightBrush;
            return;
        }

        SetBrush("WindowBackgroundBrush", darkMode ? "#151719" : "#F7F8FA");
        SetBrush("TitleBarBrush", darkMode ? "#17191C" : "#F7F8FA");
        SetBrush("SurfaceBrush", darkMode ? "#1E2124" : "#FFFFFF");
        SetBrush("PrimaryTextBrush", darkMode ? "#F5F5F5" : "#171717");
        SetBrush("SecondaryTextBrush", darkMode ? "#B5BBC4" : "#5B6472");
        SetBrush("AccentBrush", darkMode ? "#D7A63A" : "#A16207");
        SetBrush("AccentHoverBrush", darkMode ? "#E5B84D" : "#854D0E");
        SetBrush("AccentTextBrush", darkMode ? "#171717" : "#FFFFFF");
        SetBrush("BorderBrush", darkMode ? "#34383D" : "#DDE1E7");
        SetBrush("SubtleBrush", darkMode ? "#282C30" : "#ECEFF3");
        SetBrush("SubtleHoverBrush", darkMode ? "#33383D" : "#E3E7EC");
        SetBrush("SuccessBrush", darkMode ? "#4ADE80" : "#15803D");
        SetBrush("WarningBrush", darkMode ? "#FACC15" : "#A16207");
        SetBrush("ErrorBrush", darkMode ? "#F87171" : "#B91C1C");
    }

    private static void SetBrush(string key, string value)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        brush.Freeze();
        Application.Current.Resources[key] = brush;
    }
}
