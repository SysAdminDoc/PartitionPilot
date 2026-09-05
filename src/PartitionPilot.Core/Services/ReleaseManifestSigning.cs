using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PartitionPilot;

public enum ManifestTrust
{
    /// <summary>Signature verified against the embedded key, version moves forward, manifest not expired.</summary>
    Trusted,

    /// <summary>No signature or no embedded key. The channel is unprotected but nothing is provably wrong.</summary>
    Unsigned,

    /// <summary>The manifest was signed but must not be acted on.</summary>
    Rejected
}

public sealed record ManifestTrustResult(ManifestTrust Trust, string Detail)
{
    public bool CanApply => Trust != ManifestTrust.Rejected;
}

/// <summary>
/// Signs and verifies the release manifest.
/// <para>
/// Velopack checks a package's hash and size against the feed, so whoever controls the feed controls
/// both. The control that survives a compromised host is a signature over the manifest itself, verified
/// against a key that never touches the release infrastructure. Two further checks come with it: a
/// version the client refuses to move backwards from, which defeats serving an old vulnerable build, and
/// an expiry the client refuses to accept past, which defeats freezing a client on a stale manifest.
/// </para>
/// <para>
/// The signature is ECDSA P-256 over SHA-256. .NET 10 has no Ed25519 in the base class library, and
/// pulling a third-party crypto package into the update-trust path would widen exactly the supply-chain
/// surface this is meant to narrow.
/// </para>
/// </summary>
public static class ReleaseManifestSigning
{
    public const string SignatureAlgorithm = "ECDSA-P256-SHA256";

    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        WriteIndented = false,
        // Property order is fixed by the payload builder below, not by reflection, so the bytes signed on
        // the release machine and the bytes verified on the client cannot drift apart.
    };

    /// <summary>
    /// The exact bytes covered by the signature: schema, version, timestamps and every artifact's name,
    /// length and hash. Built field by field rather than by serialising the manifest, so adding a
    /// property later cannot silently change what past releases verified against.
    /// </summary>
    public static byte[] ComputeSigningPayload(ReleaseArtifactManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var payload = new
        {
            manifest.SchemaVersion,
            manifest.AppVersion,
            GeneratedAt = manifest.GeneratedAt.ToUniversalTime().ToString("O"),
            ExpiresAt = manifest.ExpiresAt?.ToUniversalTime().ToString("O") ?? "",
            manifest.IsLocalTestBuild,
            manifest.SigningStatus,
            Artifacts = manifest.Artifacts
                .OrderBy(a => a.FileName, StringComparer.Ordinal)
                .Select(a => new { a.FileName, a.Length, Sha256 = a.Sha256.ToLowerInvariant(), a.AuthenticodeStatus })
                .ToList()
        };

        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, PayloadOptions));
    }

    /// <summary>Signs <paramref name="manifest"/> in place with a PEM-encoded EC private key.</summary>
    public static void Sign(ReleaseArtifactManifest manifest, string privateKeyPem)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (string.IsNullOrWhiteSpace(privateKeyPem))
            throw new ArgumentException("A private key is required to sign a release manifest.", nameof(privateKeyPem));

        using var key = ECDsa.Create();
        key.ImportFromPem(privateKeyPem);

        manifest.SignatureAlgorithm = SignatureAlgorithm;
        manifest.Signature = "";
        manifest.Signature = Convert.ToBase64String(
            key.SignData(ComputeSigningPayload(manifest), HashAlgorithmName.SHA256));
    }

    /// <summary>
    /// Decides whether a manifest may be acted on.
    /// </summary>
    /// <param name="publicKeyPem">
    /// The key compiled into the client. Empty means the channel is not protected yet, which is reported
    /// as <see cref="ManifestTrust.Unsigned"/> rather than silently passing as trusted.
    /// </param>
    /// <param name="installedVersion">Version currently installed; the manifest may not go backwards from it.</param>
    public static ManifestTrustResult Verify(
        ReleaseArtifactManifest manifest,
        string publicKeyPem,
        string installedVersion,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (string.IsNullOrWhiteSpace(publicKeyPem))
            return new ManifestTrustResult(ManifestTrust.Unsigned,
                "No release signing key is compiled into this build, so the update manifest cannot be authenticated.");

        if (string.IsNullOrWhiteSpace(manifest.Signature))
            return new ManifestTrustResult(ManifestTrust.Rejected,
                "This build expects a signed release manifest and the manifest carries no signature.");

        if (!string.Equals(manifest.SignatureAlgorithm, SignatureAlgorithm, StringComparison.Ordinal))
            return new ManifestTrustResult(ManifestTrust.Rejected,
                $"Unsupported manifest signature algorithm '{manifest.SignatureAlgorithm}'.");

        if (!SignatureMatches(manifest, publicKeyPem))
            return new ManifestTrustResult(ManifestTrust.Rejected,
                "Release manifest signature does not match its contents. The manifest or an artifact hash was altered.");

        if (manifest.ExpiresAt is { } expiry && now > expiry)
            return new ManifestTrustResult(ManifestTrust.Rejected,
                $"Release manifest expired on {expiry.ToUniversalTime():u}. A client held on a stale manifest cannot see newer releases.");

        if (!string.IsNullOrWhiteSpace(installedVersion) &&
            !string.IsNullOrWhiteSpace(manifest.AppVersion) &&
            IsOlderVersion(manifest.AppVersion, installedVersion))
        {
            return new ManifestTrustResult(ManifestTrust.Rejected,
                $"Release manifest offers v{manifest.AppVersion}, older than the installed v{installedVersion}. " +
                "Refusing to roll back.");
        }

        return new ManifestTrustResult(ManifestTrust.Trusted,
            $"Manifest signature verified for v{manifest.AppVersion}.");
    }

    private static bool SignatureMatches(ReleaseArtifactManifest manifest, string publicKeyPem)
    {
        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(manifest.Signature);
        }
        catch (FormatException)
        {
            return false;
        }

        // The signature field is not part of what was signed, so it is cleared for the comparison and
        // restored afterwards rather than mutating the caller's manifest.
        var original = manifest.Signature;
        try
        {
            manifest.Signature = "";
            var payload = ComputeSigningPayload(manifest);

            using var key = ECDsa.Create();
            key.ImportFromPem(publicKeyPem);
            return key.VerifyData(payload, signature, HashAlgorithmName.SHA256);
        }
        catch (CryptographicException)
        {
            return false;
        }
        finally
        {
            manifest.Signature = original;
        }
    }

    internal static bool IsOlderVersion(string candidate, string baseline) =>
        Version.TryParse(Normalize(candidate), out var candidateVersion) &&
        Version.TryParse(Normalize(baseline), out var baselineVersion) &&
        candidateVersion < baselineVersion;

    private static string Normalize(string version) => version.TrimStart('v', 'V').Trim();
}
