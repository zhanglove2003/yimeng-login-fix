using System.Security.Cryptography;
using YiMengFix.Core;

namespace YiMengFix.Core.Tests;

public sealed class FixServiceTests
{
    [Fact]
    public void EmbeddedPayload_HasExpectedLengthAndHash()
    {
        var payload = new EmbeddedPayloadProvider().GetPayload();

        Assert.Equal(FixConstants.ExpectedPayloadLength, payload.Length);
        Assert.Equal(FixConstants.ExpectedPayloadHash, Convert.ToHexString(SHA256.HashData(payload)));
    }

    [Fact]
    public async Task Preflight_MissingRoot_ReturnsBlockingFailure()
    {
        var service = CreateService();
        var missingRoot = Path.Combine(Path.GetTempPath(), $"YiMengFix-Missing-{Guid.NewGuid():N}");

        var result = await service.PreflightAsync(missingRoot, CancellationToken.None);

        Assert.False(result.CanApply);
        Assert.Contains(result.Checks, check => check.Key == "game-root" && check.Status == CheckStatus.Failed);
    }

    [Fact]
    public async Task Preflight_GameRunning_BlocksApply()
    {
        using var fixture = GameFixture.Create();
        var service = CreateService(gameRunning: true);

        var result = await service.PreflightAsync(fixture.Root, CancellationToken.None);

        Assert.False(result.CanApply);
        Assert.Contains(result.Checks, check => check.Key == "process" && check.Status == CheckStatus.Failed);
    }

    [Fact]
    public async Task Apply_CreatesBackup_ReplacesFiles_AndCanRestore()
    {
        using var fixture = GameFixture.Create();
        var service = CreateService();
        var progress = new Progress<FixProgress>();

        var apply = await service.ApplyAsync(
            fixture.Root,
            progress,
            CancellationToken.None);

        Assert.True(apply.Succeeded, apply.Message);
        Assert.NotNull(apply.BackupPath);
        Assert.All(FixConstants.TargetFiles, relative =>
            Assert.Equal(FixConstants.ExpectedPayloadHash, FileHash(Path.Combine(fixture.Root, relative))));
        Assert.Equal(fixture.OriginalBytes[0], File.ReadAllBytes(Path.Combine(apply.BackupPath!, FixConstants.TargetFiles[0])));
        Assert.Equal(fixture.OriginalBytes[1], File.ReadAllBytes(Path.Combine(apply.BackupPath!, FixConstants.TargetFiles[1])));

        var backups = await service.ListBackupsAsync(fixture.Root, CancellationToken.None);
        var restore = await service.RestoreAsync(
            fixture.Root,
            backups.Single(backup => backup.Path == apply.BackupPath),
            progress,
            CancellationToken.None);

        Assert.True(restore.Succeeded, restore.Message);
        Assert.Equal(fixture.OriginalBytes[0], File.ReadAllBytes(Path.Combine(fixture.Root, FixConstants.TargetFiles[0])));
        Assert.Equal(fixture.OriginalBytes[1], File.ReadAllBytes(Path.Combine(fixture.Root, FixConstants.TargetFiles[1])));
    }

    [Fact]
    public async Task Apply_WhenSecondReplaceFails_RestoresBothOriginalFiles()
    {
        using var fixture = GameFixture.Create();
        var fileSystem = new FailOnSecondReplaceFileSystem();
        var service = new FixService(
            fileSystem,
            new StubProcessDetector(false),
            new EmbeddedPayloadProvider());

        var result = await service.ApplyAsync(
            fixture.Root,
            new Progress<FixProgress>(),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.True(result.RollbackSucceeded, result.Message);
        Assert.Equal(fixture.OriginalBytes[0], File.ReadAllBytes(Path.Combine(fixture.Root, FixConstants.TargetFiles[0])));
        Assert.Equal(fixture.OriginalBytes[1], File.ReadAllBytes(Path.Combine(fixture.Root, FixConstants.TargetFiles[1])));
    }

    [Fact]
    public async Task ListBackups_LegacyBackupWithoutManifest_IsRecognizedAsRepairBackup()
    {
        using var fixture = GameFixture.Create();
        var backupRoot = Path.Combine(fixture.Root, FixConstants.BackupDirectoryName, "20260819-120000");

        foreach (var relative in FixConstants.TargetFiles)
        {
            var destination = Path.Combine(backupRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Path.Combine(fixture.Root, relative), destination);
        }

        var service = CreateService();
        var backups = await service.ListBackupsAsync(fixture.Root, CancellationToken.None);

        var backup = Assert.Single(backups);
        Assert.True(backup.IsValid);
        Assert.Equal("fix", backup.Purpose);
    }

    [Fact]
    public async Task Preflight_WhenBothTargetsArePatched_MarksAlreadyApplied()
    {
        using var fixture = GameFixture.Create();
        var payload = new EmbeddedPayloadProvider().GetPayload();
        foreach (var relative in FixConstants.TargetFiles)
        {
            File.WriteAllBytes(Path.Combine(fixture.Root, relative), payload);
        }

        var result = await CreateService().PreflightAsync(fixture.Root, CancellationToken.None);

        Assert.True(result.IsAlreadyApplied);
        Assert.False(result.CanApply);
    }

    private static FixService CreateService(bool gameRunning = false) =>
        new(
            new DefaultFileSystem(),
            new StubProcessDetector(gameRunning),
            new EmbeddedPayloadProvider());

    private static string FileHash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private sealed class StubProcessDetector(bool isRunning) : IProcessDetector
    {
        public bool IsGameRunning(string gameRoot) => isRunning;
    }

    private sealed class FailOnSecondReplaceFileSystem : IFileSystem
    {
        private readonly DefaultFileSystem _inner = new();
        private int _replaceCount;

        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);
        public bool FileExists(string path) => _inner.FileExists(path);
        public void CreateDirectory(string path) => _inner.CreateDirectory(path);
        public void CopyFile(string source, string destination, bool overwrite) => _inner.CopyFile(source, destination, overwrite);
        public byte[] ReadAllBytes(string path) => _inner.ReadAllBytes(path);
        public void WriteAllBytes(string path, byte[] bytes) => _inner.WriteAllBytes(path, bytes);
        public void WriteAllText(string path, string contents) => _inner.WriteAllText(path, contents);
        public IEnumerable<string> EnumerateDirectories(string path) => _inner.EnumerateDirectories(path);
        public void DeleteFile(string path) => _inner.DeleteFile(path);
        public bool CanWriteDirectory(string path) => _inner.CanWriteDirectory(path);

        public void AtomicReplace(string temporaryPath, string destinationPath)
        {
            _replaceCount++;
            if (_replaceCount == 2)
            {
                throw new IOException("Injected failure on second replacement.");
            }

            _inner.AtomicReplace(temporaryPath, destinationPath);
        }
    }

    private sealed class GameFixture : IDisposable
    {
        private GameFixture(string root, byte[][] originalBytes)
        {
            Root = root;
            OriginalBytes = originalBytes;
        }

        public string Root { get; }
        public byte[][] OriginalBytes { get; }

        public static GameFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), $"YiMengFix-Tests-{Guid.NewGuid():N}");
            var originals = new[]
            {
                new byte[] { 1, 3, 3, 7, 10 },
                new byte[] { 2, 4, 6, 8, 12 },
            };

            for (var index = 0; index < FixConstants.TargetFiles.Count; index++)
            {
                var path = Path.Combine(root, FixConstants.TargetFiles[index]);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, originals[index]);
            }

            return new GameFixture(root, originals);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
