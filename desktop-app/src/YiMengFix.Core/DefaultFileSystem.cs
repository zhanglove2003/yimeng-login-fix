namespace YiMengFix.Core;

public sealed class DefaultFileSystem : IFileSystem
{
    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool FileExists(string path) => File.Exists(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void CopyFile(string source, string destination, bool overwrite) =>
        File.Copy(source, destination, overwrite);

    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    public void WriteAllBytes(string path, byte[] bytes) => File.WriteAllBytes(path, bytes);

    public void WriteAllText(string path, string contents) => File.WriteAllText(path, contents);

    public IEnumerable<string> EnumerateDirectories(string path) => Directory.EnumerateDirectories(path);

    public void AtomicReplace(string temporaryPath, string destinationPath)
    {
        try
        {
            File.Replace(temporaryPath, destinationPath, null, ignoreMetadataErrors: true);
        }
        catch (IOException)
        {
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        catch (PlatformNotSupportedException)
        {
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
    }

    public void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public bool CanWriteDirectory(string path)
    {
        var probe = Path.Combine(path, $".yimengfix-write-test-{Guid.NewGuid():N}");
        try
        {
            using var stream = new FileStream(
                probe,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            DeleteFile(probe);
        }
    }
}
