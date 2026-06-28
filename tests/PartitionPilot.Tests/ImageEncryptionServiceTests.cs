using System.Security.Cryptography;

namespace PartitionPilot.Tests;

public class ImageEncryptionServiceTests : IDisposable
{
    private const string Password = "correct horse battery staple";
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "PartitionPilotTests", Guid.NewGuid().ToString("N"));
    private readonly TestLog _log = new();

    public ImageEncryptionServiceTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task EncryptDecrypt_RoundTripsChunkedImage()
    {
        var source = PathFor("source.bin");
        var encrypted = PathFor("source.bin.enc");
        var decrypted = PathFor("source.roundtrip.bin");
        var original = RandomNumberGenerator.GetBytes(5 * 1024 * 1024 + 123);
        await File.WriteAllBytesAsync(source, original);

        await ImageEncryptionService.EncryptFileAsync(source, encrypted, Password, _log);
        await ImageEncryptionService.DecryptFileAsync(encrypted, decrypted, Password, _log);

        Assert.True(ImageEncryptionService.IsEncryptedImage(encrypted));
        Assert.Equal(original, await File.ReadAllBytesAsync(decrypted));
    }

    [Fact]
    public async Task DecryptFileAsync_ReadsLegacyPpenc1Images()
    {
        var encrypted = PathFor("legacy.enc");
        var decrypted = PathFor("legacy.bin");
        var original = RandomNumberGenerator.GetBytes(4097);
        await WriteLegacyEncryptedImageAsync(encrypted, original, Password);

        await ImageEncryptionService.DecryptFileAsync(encrypted, decrypted, Password, _log);

        Assert.True(ImageEncryptionService.IsEncryptedImage(encrypted));
        Assert.Equal(original, await File.ReadAllBytesAsync(decrypted));
    }

    [Fact]
    public async Task DecryptFileAsync_RejectsTamperedChunk()
    {
        var source = PathFor("tamper.bin");
        var encrypted = PathFor("tamper.bin.enc");
        await File.WriteAllBytesAsync(source, RandomNumberGenerator.GetBytes(1024 * 1024 + 9));
        await ImageEncryptionService.EncryptFileAsync(source, encrypted, Password, _log);

        await using (var stream = new FileStream(encrypted, FileMode.Open, FileAccess.ReadWrite))
        {
            stream.Position = stream.Length - 1;
            var value = stream.ReadByte();
            stream.Position = stream.Length - 1;
            stream.WriteByte((byte)(value ^ 0xFF));
        }

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ImageEncryptionService.DecryptFileAsync(encrypted, PathFor("tamper.out"), Password, _log));
        Assert.Contains("incorrect password or corrupted file", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DecryptFileAsync_RejectsWrongPassword()
    {
        var source = PathFor("wrong-password.bin");
        var encrypted = PathFor("wrong-password.bin.enc");
        await File.WriteAllBytesAsync(source, RandomNumberGenerator.GetBytes(64 * 1024));
        await ImageEncryptionService.EncryptFileAsync(source, encrypted, Password, _log);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ImageEncryptionService.DecryptFileAsync(encrypted, PathFor("wrong-password.out"), "wrong", _log));
        Assert.Contains("incorrect password or corrupted file", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EncryptFileAsync_HonorsCancellationBeforeWriting()
    {
        var source = PathFor("cancel.bin");
        var encrypted = PathFor("cancel.bin.enc");
        await File.WriteAllBytesAsync(source, RandomNumberGenerator.GetBytes(4096));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            ImageEncryptionService.EncryptFileAsync(source, encrypted, Password, _log, ct: cts.Token));

        Assert.False(File.Exists(encrypted));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string PathFor(string fileName) => Path.Combine(_tempDir, fileName);

    private static async Task WriteLegacyEncryptedImageAsync(string path, byte[] plaintext, string password)
    {
        const int saltSize = 16;
        const int nonceSize = 12;
        const int tagSize = 16;
        const int keySize = 32;
        const int iterations = 600000;

        var salt = RandomNumberGenerator.GetBytes(saltSize);
        var nonce = RandomNumberGenerator.GetBytes(nonceSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, keySize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[tagSize];

        using var aes = new AesGcm(key, tagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        await using var output = File.Create(path);
        await output.WriteAsync("PPENC1"u8.ToArray());
        await output.WriteAsync(salt);
        await output.WriteAsync(nonce);
        await output.WriteAsync(tag);
        await output.WriteAsync(ciphertext);
    }

    private sealed class TestLog : IActivityLog
    {
        public void Log(string message) { }
    }
}
