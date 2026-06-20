using System.Security.Cryptography;

namespace PartitionPilot;

public static class ImageEncryptionService
{
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 600000;
    private static readonly byte[] Magic = "PPENC1"u8.ToArray();

    public static async Task EncryptFileAsync(string sourcePath, string destPath, string password,
        IActivityLog log, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = DeriveKey(password, salt);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);

        log.Log($"Encrypting image to {Path.GetFileName(destPath)}...");

        var plaintext = await File.ReadAllBytesAsync(sourcePath, ct);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        await using var output = File.Create(destPath);
        await output.WriteAsync(Magic, ct);
        await output.WriteAsync(salt, ct);
        await output.WriteAsync(nonce, ct);
        await output.WriteAsync(tag, ct);
        await output.WriteAsync(ciphertext, ct);

        progress?.Report(100);
        log.Log($"Image encrypted: {SizeUtil.Format(plaintext.Length)} -> {Path.GetFileName(destPath)}");
    }

    public static async Task DecryptFileAsync(string sourcePath, string destPath, string password,
        IActivityLog log, CancellationToken ct = default)
    {
        log.Log($"Decrypting image {Path.GetFileName(sourcePath)}...");

        var data = await File.ReadAllBytesAsync(sourcePath, ct);

        if (data.Length < Magic.Length + SaltSize + NonceSize + TagSize)
            throw new InvalidOperationException("File is too small to be a valid encrypted image.");

        if (!data.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new InvalidOperationException("File is not a PartitionPilot encrypted image.");

        int offset = Magic.Length;
        var salt = data.AsSpan(offset, SaltSize).ToArray();
        offset += SaltSize;
        var nonce = data.AsSpan(offset, NonceSize).ToArray();
        offset += NonceSize;
        var tag = data.AsSpan(offset, TagSize).ToArray();
        offset += TagSize;
        var ciphertext = data.AsSpan(offset).ToArray();

        var key = DeriveKey(password, salt);
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, TagSize);
        try
        {
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("Decryption failed — incorrect password or corrupted file.");
        }

        await File.WriteAllBytesAsync(destPath, plaintext, ct);
        log.Log($"Image decrypted: {SizeUtil.Format(plaintext.Length)} -> {Path.GetFileName(destPath)}");
    }

    public static bool IsEncryptedImage(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var header = new byte[Magic.Length];
            return fs.Read(header) == Magic.Length && header.AsSpan().SequenceEqual(Magic);
        }
        catch { return false; }
    }

    private static byte[] DeriveKey(string password, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
    }
}
