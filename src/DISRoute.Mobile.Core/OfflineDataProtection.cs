using System.Security.Cryptography;

namespace DISRoute.Mobile.Core;

public interface IOfflineDataProtector
{
    byte[] Protect(ReadOnlySpan<byte> plaintext);
    byte[] Unprotect(ReadOnlySpan<byte> protectedData);
}

public sealed class AesGcmOfflineDataProtector : IOfflineDataProtector, IDisposable
{
    private const byte FormatVersion = 1;
    private readonly byte[] _key;

    public AesGcmOfflineDataProtector(ReadOnlySpan<byte> key)
    {
        if (key.Length is not (16 or 24 or 32)) throw new ArgumentException("AES key must be 128, 192, or 256 bits.", nameof(key));
        _key = key.ToArray();
    }

    public static AesGcmOfflineDataProtector FromBase64Key(string base64Key) => new(Convert.FromBase64String(base64Key));

    public byte[] Protect(ReadOnlySpan<byte> plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var ciphertext = new byte[plaintext.Length];
        using var aes = new AesGcm(_key, tag.Length);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, [FormatVersion]);
        var output = new byte[1 + nonce.Length + tag.Length + ciphertext.Length];
        output[0] = FormatVersion;
        nonce.CopyTo(output, 1);
        tag.CopyTo(output, 13);
        ciphertext.CopyTo(output, 29);
        return output;
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedData)
    {
        if (protectedData.Length < 29 || protectedData[0] != FormatVersion) throw new CryptographicException("Unsupported or invalid DISRoute offline cache format.");
        var nonce = protectedData.Slice(1, 12);
        var tag = protectedData.Slice(13, 16);
        var ciphertext = protectedData[29..];
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(_key, tag.Length);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, [FormatVersion]);
        return plaintext;
    }

    public void Dispose() => CryptographicOperations.ZeroMemory(_key);
}
