namespace YiMengFix.Core;

public enum CheckStatus
{
    Passed,
    Warning,
    Failed,
}

public enum FixStage
{
    Preflight,
    BackingUp,
    Replacing,
    Verifying,
    Restoring,
    Completed,
}

public sealed record PreflightCheck(
    string Key,
    string Label,
    CheckStatus Status,
    string Detail,
    bool BlocksOperation = false);

public sealed record PreflightResult(
    string GameRoot,
    IReadOnlyList<PreflightCheck> Checks,
    bool IsAlreadyApplied)
{
    public bool CanApply =>
        !IsAlreadyApplied &&
        Checks.All(check => !check.BlocksOperation || check.Status == CheckStatus.Passed);
}

public sealed record FixProgress(
    FixStage Stage,
    int CompletedSteps,
    int TotalSteps,
    string Message);

public sealed record FixResult(
    bool Succeeded,
    string? BackupPath,
    IReadOnlyList<string> UpdatedFiles,
    string Message,
    bool RollbackSucceeded = true);

public sealed record BackupInfo(
    string Name,
    string Path,
    DateTime? CreatedAt,
    bool IsValid,
    string Purpose = "unknown")
{
    public override string ToString() => IsValid ? Name : $"{Name}（不完整）";
}

public sealed record RestoreResult(
    bool Succeeded,
    string? SafetyBackupPath,
    string Message,
    bool RollbackSucceeded = true);

public interface IFixService
{
    Task<PreflightResult> PreflightAsync(
        string gameRoot,
        CancellationToken cancellationToken);

    Task<FixResult> ApplyAsync(
        string gameRoot,
        IProgress<FixProgress> progress,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(
        string gameRoot,
        CancellationToken cancellationToken);

    Task<RestoreResult> RestoreAsync(
        string gameRoot,
        BackupInfo backup,
        IProgress<FixProgress> progress,
        CancellationToken cancellationToken);
}

public interface IProcessDetector
{
    bool IsGameRunning(string gameRoot);
}

public interface IPayloadProvider
{
    byte[] GetPayload();
}

public interface IFileSystem
{
    bool DirectoryExists(string path);
    bool FileExists(string path);
    void CreateDirectory(string path);
    void CopyFile(string source, string destination, bool overwrite);
    byte[] ReadAllBytes(string path);
    void WriteAllBytes(string path, byte[] bytes);
    void WriteAllText(string path, string contents);
    IEnumerable<string> EnumerateDirectories(string path);
    void AtomicReplace(string temporaryPath, string destinationPath);
    void DeleteFile(string path);
    bool CanWriteDirectory(string path);
}
