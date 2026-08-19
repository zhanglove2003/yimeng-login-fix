using System.Security.Cryptography;

namespace YiMengFix.Core;

public sealed class EmbeddedPayloadProvider : IPayloadProvider
{
    private readonly Lazy<byte[]> _payload = new(LoadAndValidate);

    public byte[] GetPayload() => (byte[])_payload.Value.Clone();

    private static byte[] LoadAndValidate()
    {
        var assembly = typeof(EmbeddedPayloadProvider).Assembly;
        var resourceName = assembly
            .GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith(".Payload.b64", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("找不到内置修复数据。请重新下载程序。");

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("无法读取内置修复数据。请重新下载程序。");
        using var reader = new StreamReader(stream);
        var bytes = Convert.FromBase64String(reader.ReadToEnd().Trim());
        var hash = Convert.ToHexString(SHA256.HashData(bytes));

        if (bytes.Length != FixConstants.ExpectedPayloadLength ||
            !hash.Equals(FixConstants.ExpectedPayloadHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"内置修复数据校验失败。长度={bytes.Length}，SHA256={hash}");
        }

        return bytes;
    }
}
