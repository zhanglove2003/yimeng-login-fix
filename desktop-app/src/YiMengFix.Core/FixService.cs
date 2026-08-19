using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace YiMengFix.Core;

public sealed class FixService : IFixService
{
    private const int TotalApplySteps = 7;
    private readonly IFileSystem _fileSystem;
    private readonly IProcessDetector _processDetector;
    private readonly IPayloadProvider _payloadProvider;
    private readonly Action<string>? _log;

    public FixService(
        IFileSystem? fileSystem = null,
        IProcessDetector? processDetector = null,
        IPayloadProvider? payloadProvider = null,
        Action<string>? log = null)
    {
        _fileSystem = fileSystem ?? new DefaultFileSystem();
        _processDetector = processDetector ?? new WindowsProcessDetector();
        _payloadProvider = payloadProvider ?? new EmbeddedPayloadProvider();
        _log = log;
    }

    public Task<PreflightResult> PreflightAsync(string gameRoot, CancellationToken cancellationToken) =>
        Task.Run(() => BuildPreflight(gameRoot, cancellationToken), cancellationToken);

    public Task<FixResult> ApplyAsync(
        string gameRoot,
        IProgress<FixProgress> progress,
        CancellationToken cancellationToken) =>
        Task.Run(() => ApplyCore(gameRoot, progress, cancellationToken), cancellationToken);

    public Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(
        string gameRoot,
        CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<BackupInfo>>(() => ListBackupsCore(gameRoot, cancellationToken), cancellationToken);

    public Task<RestoreResult> RestoreAsync(
        string gameRoot,
        BackupInfo backup,
        IProgress<FixProgress> progress,
        CancellationToken cancellationToken) =>
        Task.Run(() => RestoreCore(gameRoot, backup, progress, cancellationToken), cancellationToken);

    private PreflightResult BuildPreflight(string gameRoot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = NormalizeRoot(gameRoot);
        var checks = new List<PreflightCheck>();

        var rootExists = _fileSystem.DirectoryExists(root);
        checks.Add(new PreflightCheck(
            "game-root",
            "游戏目录",
            rootExists ? CheckStatus.Passed : CheckStatus.Failed,
            rootExists ? root : "目录不存在，请重新选择。",
            BlocksOperation: true));

        try
        {
            var payload = _payloadProvider.GetPayload();
            checks.Add(new PreflightCheck(
                "payload",
                "修复数据",
                CheckStatus.Passed,
                $"已通过完整性校验（{payload.Length} 字节）。",
                BlocksOperation: true));
        }
        catch (Exception ex)
        {
            checks.Add(new PreflightCheck(
                "payload",
                "修复数据",
                CheckStatus.Failed,
                ex.Message,
                BlocksOperation: true));
        }

        if (!rootExists)
        {
            foreach (var relative in FixConstants.TargetFiles)
            {
                checks.Add(new PreflightCheck(relative, relative, CheckStatus.Failed, "无法检查：游戏目录不存在。", true));
            }

            checks.Add(new PreflightCheck("process", "游戏进程", CheckStatus.Warning, "未检查。", true));
            checks.Add(new PreflightCheck("write", "写入权限", CheckStatus.Warning, "未检查。", true));
            return new PreflightResult(root, checks, IsAlreadyApplied: false);
        }

        var alreadyAppliedCount = 0;
        foreach (var relative in FixConstants.TargetFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.Combine(root, relative);
            if (!_fileSystem.FileExists(target))
            {
                checks.Add(new PreflightCheck(relative, relative, CheckStatus.Failed, "目标文件不存在。", true));
                continue;
            }

            var hash = GetFileHash(target);
            var alreadyApplied = hash.Equals(FixConstants.ExpectedPayloadHash, StringComparison.OrdinalIgnoreCase);
            if (alreadyApplied)
            {
                alreadyAppliedCount++;
            }

            checks.Add(new PreflightCheck(
                relative,
                relative,
                CheckStatus.Passed,
                alreadyApplied ? "已经是目标版本。" : $"文件有效，当前 SHA256：{ShortHash(hash)}",
                BlocksOperation: true));
        }

        var gameRunning = _processDetector.IsGameRunning(root);
        checks.Add(new PreflightCheck(
            "process",
            "游戏进程",
            gameRunning ? CheckStatus.Failed : CheckStatus.Passed,
            gameRunning ? "检测到游戏或启动器正在运行，请先关闭。" : "未检测到正在运行的游戏或启动器。",
            BlocksOperation: true));

        var canWrite = _fileSystem.CanWriteDirectory(root);
        checks.Add(new PreflightCheck(
            "write",
            "写入权限",
            canWrite ? CheckStatus.Passed : CheckStatus.Failed,
            canWrite ? "当前权限可以写入游戏目录。" : "无法写入游戏目录。",
            BlocksOperation: true));

        return new PreflightResult(
            root,
            checks,
            IsAlreadyApplied: alreadyAppliedCount == FixConstants.TargetFiles.Count);
    }

    private FixResult ApplyCore(
        string gameRoot,
        IProgress<FixProgress> progress,
        CancellationToken cancellationToken)
    {
        var root = NormalizeRoot(gameRoot);
        string? backupRoot = null;
        var updatedFiles = new List<string>();

        try
        {
            Report(progress, FixStage.Preflight, 1, TotalApplySteps, "正在执行安全检查…");
            var preflight = BuildPreflight(root, cancellationToken);
            LogPreflight(preflight);

            if (preflight.IsAlreadyApplied)
            {
                return new FixResult(true, null, [], "目标文件已经是修复版本，无需重复处理。");
            }

            if (!preflight.CanApply)
            {
                var reason = preflight.Checks.First(check => check.BlocksOperation && check.Status != CheckStatus.Passed);
                return new FixResult(false, null, [], reason.Detail);
            }

            var payload = _payloadProvider.GetPayload();
            Report(progress, FixStage.BackingUp, 2, TotalApplySteps, "正在创建备份…");
            backupRoot = CreateBackup(root, "fix", cancellationToken);

            var step = 3;
            foreach (var relative in FixConstants.TargetFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Report(progress, FixStage.Replacing, step++, TotalApplySteps, $"正在替换 {relative}…");
                ReplaceWithBytes(root, relative, payload, FixConstants.ExpectedPayloadHash);
                updatedFiles.Add(relative);
                Emit($"[OK] 已替换 {relative}");
            }

            Report(progress, FixStage.Verifying, 6, TotalApplySteps, "正在验证目标文件…");
            foreach (var relative in FixConstants.TargetFiles)
            {
                var targetHash = GetFileHash(Path.Combine(root, relative));
                if (!targetHash.Equals(FixConstants.ExpectedPayloadHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException($"目标文件校验失败：{relative}");
                }
            }

            Report(progress, FixStage.Completed, 7, TotalApplySteps, "修复已完成。");
            Emit($"修复完成。备份：{backupRoot}");
            return new FixResult(true, backupRoot, updatedFiles, "修复完成，可以启动游戏。", true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Emit($"修复失败：{ex.Message}");
            var rollbackSucceeded = backupRoot is not null && TryRestoreFromBackup(root, backupRoot, cancellationToken);
            var rollbackText = rollbackSucceeded ? "已自动恢复原文件。" : "自动恢复未完成，请使用备份手动恢复。";
            return new FixResult(false, backupRoot, updatedFiles, $"{ex.Message} {rollbackText}", rollbackSucceeded);
        }
    }

    private IReadOnlyList<BackupInfo> ListBackupsCore(string gameRoot, CancellationToken cancellationToken)
    {
        var root = NormalizeRoot(gameRoot);
        var backupBase = Path.Combine(root, FixConstants.BackupDirectoryName);
        if (!_fileSystem.DirectoryExists(backupBase))
        {
            return [];
        }

        return _fileSystem
            .EnumerateDirectories(backupBase)
            .Select(path =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileName(path);
                var valid = FixConstants.TargetFiles.All(relative => _fileSystem.FileExists(Path.Combine(path, relative)));
                var parsed = TryParseBackupTimestamp(name);
                var purpose = ReadBackupPurpose(path);
                return new BackupInfo(name, path, parsed, valid, purpose);
            })
            .OrderByDescending(backup => backup.CreatedAt)
            .ThenByDescending(backup => backup.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private string ReadBackupPurpose(string backupPath)
    {
        var manifestPath = Path.Combine(backupPath, "manifest.json");
        if (!_fileSystem.FileExists(manifestPath))
        {
            // Backups created by the original script predate manifest.json.
            return "fix";
        }

        try
        {
            using var document = JsonDocument.Parse(_fileSystem.ReadAllBytes(manifestPath));
            return document.RootElement.TryGetProperty("Purpose", out var purpose)
                ? purpose.GetString() ?? "unknown"
                : "unknown";
        }
        catch (JsonException)
        {
            return "fix";
        }
    }

    private RestoreResult RestoreCore(
        string gameRoot,
        BackupInfo backup,
        IProgress<FixProgress> progress,
        CancellationToken cancellationToken)
    {
        var root = NormalizeRoot(gameRoot);
        string? safetyBackup = null;

        try
        {
            if (!_fileSystem.DirectoryExists(root))
            {
                return new RestoreResult(false, null, "游戏目录不存在。");
            }

            if (_processDetector.IsGameRunning(root))
            {
                return new RestoreResult(false, null, "游戏或启动器正在运行，请关闭后重试。");
            }

            if (!backup.IsValid || FixConstants.TargetFiles.Any(relative => !_fileSystem.FileExists(Path.Combine(backup.Path, relative))))
            {
                return new RestoreResult(false, null, "所选备份不完整，无法恢复。");
            }

            Report(progress, FixStage.BackingUp, 1, 5, "正在备份当前文件…");
            safetyBackup = CreateBackup(root, "restore-guard", cancellationToken);

            var step = 2;
            foreach (var relative in FixConstants.TargetFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Report(progress, FixStage.Restoring, step++, 5, $"正在恢复 {relative}…");
                var source = Path.Combine(backup.Path, relative);
                var bytes = _fileSystem.ReadAllBytes(source);
                ReplaceWithBytes(root, relative, bytes, ComputeHash(bytes));
            }

            Report(progress, FixStage.Verifying, 4, 5, "正在验证恢复结果…");
            foreach (var relative in FixConstants.TargetFiles)
            {
                var sourceHash = GetFileHash(Path.Combine(backup.Path, relative));
                var targetHash = GetFileHash(Path.Combine(root, relative));
                if (!sourceHash.Equals(targetHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException($"恢复后校验失败：{relative}");
                }
            }

            Report(progress, FixStage.Completed, 5, 5, "恢复完成。");
            Emit($"已从备份恢复：{backup.Path}");
            return new RestoreResult(true, safetyBackup, "恢复完成。当前文件也已另行备份。", true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Emit($"恢复失败：{ex.Message}");
            var rollbackSucceeded = safetyBackup is not null && TryRestoreFromBackup(root, safetyBackup, cancellationToken);
            var rollbackText = rollbackSucceeded ? "已恢复到操作前状态。" : "无法自动恢复到操作前状态。";
            return new RestoreResult(false, safetyBackup, $"{ex.Message} {rollbackText}", rollbackSucceeded);
        }
    }

    private string CreateBackup(string root, string purpose, CancellationToken cancellationToken)
    {
        var backupBase = Path.Combine(root, FixConstants.BackupDirectoryName);
        _fileSystem.CreateDirectory(backupBase);

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var backupRoot = Path.Combine(backupBase, stamp);
        var suffix = 1;
        while (_fileSystem.DirectoryExists(backupRoot))
        {
            backupRoot = Path.Combine(backupBase, $"{stamp}-{suffix++:00}");
        }

        _fileSystem.CreateDirectory(backupRoot);
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var relative in FixConstants.TargetFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = Path.Combine(root, relative);
            if (!_fileSystem.FileExists(source))
            {
                throw new FileNotFoundException("目标文件不存在，无法创建备份。", source);
            }

            var destination = Path.Combine(backupRoot, relative);
            _fileSystem.CreateDirectory(Path.GetDirectoryName(destination)!);
            _fileSystem.CopyFile(source, destination, overwrite: false);
            hashes[relative] = GetFileHash(destination);
        }

        var manifest = new BackupManifest(DateTimeOffset.Now, purpose, hashes);
        _fileSystem.WriteAllText(
            Path.Combine(backupRoot, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        Emit($"已创建备份：{backupRoot}");
        return backupRoot;
    }

    private void ReplaceWithBytes(string root, string relative, byte[] bytes, string expectedHash)
    {
        var target = Path.Combine(root, relative);
        var temporary = $"{target}.yimengfix-{Guid.NewGuid():N}.tmp";
        try
        {
            _fileSystem.WriteAllBytes(temporary, bytes);
            var temporaryHash = GetFileHash(temporary);
            if (!temporaryHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException($"临时文件校验失败：{relative}");
            }

            _fileSystem.AtomicReplace(temporary, target);
            var targetHash = GetFileHash(target);
            if (!targetHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException($"目标文件校验失败：{relative}");
            }
        }
        finally
        {
            _fileSystem.DeleteFile(temporary);
        }
    }

    private bool TryRestoreFromBackup(string root, string backupRoot, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var relative in FixConstants.TargetFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = Path.Combine(backupRoot, relative);
                if (!_fileSystem.FileExists(source))
                {
                    return false;
                }

                var bytes = _fileSystem.ReadAllBytes(source);
                ReplaceWithBytes(root, relative, bytes, ComputeHash(bytes));
            }

            Emit($"已自动恢复：{backupRoot}");
            return true;
        }
        catch (Exception ex)
        {
            Emit($"自动恢复失败：{ex.Message}");
            return false;
        }
    }

    private string GetFileHash(string path) => ComputeHash(_fileSystem.ReadAllBytes(path));

    private static string ComputeHash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static string NormalizeRoot(string gameRoot)
    {
        var trimmed = (gameRoot ?? string.Empty).Trim().Trim('"');
        return string.IsNullOrWhiteSpace(trimmed) ? FixConstants.DefaultGameRoot : Path.GetFullPath(trimmed);
    }

    private static string ShortHash(string hash) => hash.Length <= 16 ? hash : $"{hash[..8]}…{hash[^8..]}";

    private static DateTime? TryParseBackupTimestamp(string name)
    {
        var timestamp = name.Length >= 15 ? name[..15] : name;
        return DateTime.TryParseExact(
            timestamp,
            "yyyyMMdd-HHmmss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : null;
    }

    private void LogPreflight(PreflightResult result)
    {
        Emit($"游戏目录：{result.GameRoot}");
        foreach (var check in result.Checks)
        {
            Emit($"[{check.Status}] {check.Label}: {check.Detail}");
        }
    }

    private void Emit(string message) => _log?.Invoke($"{DateTime.Now:HH:mm:ss}  {message}");

    private static void Report(
        IProgress<FixProgress> progress,
        FixStage stage,
        int completed,
        int total,
        string message) => progress.Report(new FixProgress(stage, completed, total, message));

    private sealed record BackupManifest(
        DateTimeOffset CreatedAt,
        string Purpose,
        IReadOnlyDictionary<string, string> Hashes);
}
