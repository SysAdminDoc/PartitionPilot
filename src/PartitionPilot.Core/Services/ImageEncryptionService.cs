using System.Buffers.Binary;
using System.Security.Cryptography;

namespace PartitionPilot;

public static class ImageEncryptionService
{
    private const int DefaultChunkSize = 4 * 1024 * 1024;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 600000;
    private static readonly byte[] LegacyMagic = "PPENC1"u8.ToArray();
    private static readonly byte[] ChunkedMagic = "PPENC2"u8.ToArray();

    public static async Task EncryptFileAsync(string sourcePath, string destPath, string password,
        IActivityLog log, IProgress<double>? progress = null, CancellationToken ct = default) =>
        await EncryptFileAsync(sourcePath, destPath, password, log, progress, ct, DefaultChunkSize);

    internal static async Task EncryptFileAsync(string sourcePath, string destPath, string password,
        IActivityLog log, IProgress<double>? progress, CancellationToken ct, int chunkSize)
    {
        ct.ThrowIfCancellationRequested();
        if (chunkSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be positive.");

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = DeriveKey(password, salt);
        var sourceLength = new FileInfo(sourcePath).Length;

        log.Log($"Encrypting image to {Path.GetFileName(destPath)}...");

        await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, chunkSize, FileOptions.SequentialScan);
        await using var output = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, chunkSize, FileOptions.SequentialScan);

        await output.WriteAsync(ChunkedMagic, ct);
        await output.WriteAsync(salt, ct);
        await WriteInt32Async(output, chunkSize, ct);
        await WriteInt64Async(output, sourceLength, ct);

        using var aes = new AesGcm(key, TagSize);
        var plaintext = new byte[chunkSize];
        var ciphertext = new byte[chunkSize];
        var tag = new byte[TagSize];
        long processed = 0;
        long chunkIndex = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var read = await input.ReadAsync(plaintext.AsMemory(0, plaintext.Length), ct);
            if (read == 0)
                break;

            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var aad = BuildAad(sourceLength, chunkSize, chunkIndex, read);
            aes.Encrypt(nonce, plaintext.AsSpan(0, read), ciphertext.AsSpan(0, read), tag, aad);

            await output.WriteAsync(nonce, ct);
            await WriteInt32Async(output, read, ct);
            await output.WriteAsync(tag, ct);
            await output.WriteAsync(ciphertext.AsMemory(0, read), ct);

            processed += read;
            chunkIndex++;
            progress?.Report(sourceLength == 0 ? 100 : processed * 100.0 / sourceLength);
        }

        progress?.Report(100);
        log.Log($"Image encrypted: {SizeUtil.Format(sourceLength)} -> {Path.GetFileName(destPath)}");
    }

    public static async Task DecryptFileAsync(string sourcePath, string destPath, string password,
        IActivityLog log, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        log.Log($"Decrypting image {Path.GetFileName(sourcePath)}...");

        await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, DefaultChunkSize, FileOptions.SequentialScan);
        var magic = new byte[ChunkedMagic.Length];
        await ReadExactlyAsync(input, magic, ct);

        if (magic.AsSpan().SequenceEqual(ChunkedMagic))
        {
            await DecryptChunkedAsync(input, destPath, password, log, ct);
            return;
        }

        if (!magic.AsSpan().SequenceEqual(LegacyMagic))
            throw new InvalidOperationException("File is not a PartitionPilot encrypted image.");

        await DecryptLegacyAsync(sourcePath, destPath, password, log, ct);
    }

    public static bool IsEncryptedImage(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var header = new byte[ChunkedMagic.Length];
            return fs.Read(header) == ChunkedMagic.Length &&
                   (header.AsSpan().SequenceEqual(ChunkedMagic) || header.AsSpan().SequenceEqual(LegacyMagic));
        }
        catch { return false; }
    }

    private static async Task DecryptChunkedAsync(Stream input, string destPath, string password,
        IActivityLog log, CancellationToken ct)
    {
        var salt = new byte[SaltSize];
        await ReadExactlyAsync(input, salt, ct);
        var chunkSize = await ReadInt32Async(input, ct);
        var plaintextLength = await ReadInt64Async(input, ct);

        if (chunkSize <= 0 || chunkSize > 64 * 1024 * 1024)
            throw new InvalidOperationException("Encrypted image chunk size is invalid.");
        if (plaintextLength < 0)
            throw new InvalidOperationException("Encrypted image length is invalid.");

        var key = DeriveKey(password, salt);
        using var aes = new AesGcm(key, TagSize);
        await using var output = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, chunkSize, FileOptions.SequentialScan);

        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var ciphertext = new byte[chunkSize];
        var plaintext = new byte[chunkSize];
        long remaining = plaintextLength;
        long chunkIndex = 0;

        try
        {
            while (remaining > 0)
            {
                ct.ThrowIfCancellationRequested();
                var expectedLength = (int)Math.Min(chunkSize, remaining);
                await ReadExactlyAsync(input, nonce, ct);
                var actualLength = await ReadInt32Async(input, ct);
                if (actualLength != expectedLength)
                    throw new InvalidOperationException("Encrypted image chunk length is invalid.");
                await ReadExactlyAsync(input, tag, ct);
                await ReadExactlyAsync(input, ciphertext.AsMemory(0, actualLength), ct);

                var aad = BuildAad(plaintextLength, chunkSize, chunkIndex, actualLength);
                aes.Decrypt(nonce, ciphertext.AsSpan(0, actualLength), tag, plaintext.AsSpan(0, actualLength), aad);
                await output.WriteAsync(plaintext.AsMemory(0, actualLength), ct);

                remaining -= actualLength;
                chunkIndex++;
            }
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("Decryption failed - incorrect password or corrupted file.");
        }

        if (input.ReadByte() != -1)
            throw new InvalidOperationException("Encrypted image has unexpected trailing data.");

        log.Log($"Image decrypted: {SizeUtil.Format(plaintextLength)} -> {Path.GetFileName(destPath)}");
    }

    private static async Task DecryptLegacyAsync(string sourcePath, string destPath, string password,
        IActivityLog log, CancellationToken ct)
    {
        var data = await File.ReadAllBytesAsync(sourcePath, ct);

        if (data.Length < LegacyMagic.Length + SaltSize + NonceSize + TagSize)
            throw new InvalidOperationException("File is too small to be a valid encrypted image.");

        int offset = LegacyMagic.Length;
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
            throw new InvalidOperationException("Decryption failed - incorrect password or corrupted file.");
        }

        await File.WriteAllBytesAsync(destPath, plaintext, ct);
        log.Log($"Image decrypted: {SizeUtil.Format(plaintext.Length)} -> {Path.GetFileName(destPath)}");
    }

    private static async Task ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken ct)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[totalRead..], ct);
            if (read == 0)
                throw new InvalidOperationException("Encrypted image ended unexpectedly.");
            totalRead += read;
        }
    }

    private static async Task WriteInt32Async(Stream stream, int value, CancellationToken ct)
    {
        var buffer = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        await stream.WriteAsync(buffer, ct);
    }

    private static async Task<int> ReadInt32Async(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[sizeof(int)];
        await ReadExactlyAsync(stream, buffer, ct);
        return BinaryPrimitives.ReadInt32LittleEndian(buffer);
    }

    private static async Task WriteInt64Async(Stream stream, long value, CancellationToken ct)
    {
        var buffer = new byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
        await stream.WriteAsync(buffer, ct);
    }

    private static async Task<long> ReadInt64Async(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[sizeof(long)];
        await ReadExactlyAsync(stream, buffer, ct);
        return BinaryPrimitives.ReadInt64LittleEndian(buffer);
    }

    private static byte[] BuildAad(long plaintextLength, int chunkSize, long chunkIndex, int chunkLength)
    {
        var aad = new byte[ChunkedMagic.Length + sizeof(long) + sizeof(int) + sizeof(long) + sizeof(int)];
        var offset = 0;
        ChunkedMagic.CopyTo(aad, offset);
        offset += ChunkedMagic.Length;
        BinaryPrimitives.WriteInt64LittleEndian(aad.AsSpan(offset, sizeof(long)), plaintextLength);
        offset += sizeof(long);
        BinaryPrimitives.WriteInt32LittleEndian(aad.AsSpan(offset, sizeof(int)), chunkSize);
        offset += sizeof(int);
        BinaryPrimitives.WriteInt64LittleEndian(aad.AsSpan(offset, sizeof(long)), chunkIndex);
        offset += sizeof(long);
        BinaryPrimitives.WriteInt32LittleEndian(aad.AsSpan(offset, sizeof(int)), chunkLength);
        return aad;
    }

    private static byte[] DeriveKey(string password, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
    }
}
