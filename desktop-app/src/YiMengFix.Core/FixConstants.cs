namespace YiMengFix.Core;

public static class FixConstants
{
    public const string DefaultGameRoot = @"D:\wyclx";
    public const string ExpectedPayloadHash = "535854FB2ABE71E16F645A879F1C00144FD9BA71548DE44B8E51E049BEAD2D87";
    public const int ExpectedPayloadLength = 400;
    public const string BackupDirectoryName = "_tmsoft_fix_backup";

    public static IReadOnlyList<string> TargetFiles { get; } =
    [
        @"Engine\Binaries\Win32\netease.data",
        @"Engine\Binaries\Win64\netease.data",
    ];

    public static IReadOnlyList<string> ProcessNames { get; } =
    [
        "wyclx64",
        "wyclx",
        "Launcher",
    ];
}
