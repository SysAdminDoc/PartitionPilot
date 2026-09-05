using System.Security.Cryptography;

namespace PartitionPilot.Tests;

public class ReleaseManifestSigningTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Verify_AcceptsAManifestSignedByTheMatchingKey()
    {
        var (privateKey, publicKey) = KeyPair();
        var manifest = Manifest();
        ReleaseManifestSigning.Sign(manifest, privateKey);

        var result = ReleaseManifestSigning.Verify(manifest, publicKey, "0.9.22", Now);

        Assert.Equal(ManifestTrust.Trusted, result.Trust);
        Assert.True(result.CanApply);
    }

    [Fact]
    public void Verify_RejectsAManifestWhoseArtifactHashWasAltered()
    {
        var (privateKey, publicKey) = KeyPair();
        var manifest = Manifest();
        ReleaseManifestSigning.Sign(manifest, privateKey);

        // Exactly the attack the signature exists to stop: the feed still advertises a matching hash, but
        // it is the hash of a different payload.
        manifest.Artifacts[0].Sha256 = new string('b', 64);

        var result = ReleaseManifestSigning.Verify(manifest, publicKey, "0.9.22", Now);

        Assert.Equal(ManifestTrust.Rejected, result.Trust);
        Assert.Contains("does not match its contents", result.Detail);
        Assert.False(result.CanApply);
    }

    [Fact]
    public void Verify_RejectsAnAddedArtifact()
    {
        var (privateKey, publicKey) = KeyPair();
        var manifest = Manifest();
        ReleaseManifestSigning.Sign(manifest, privateKey);

        manifest.Artifacts.Add(new ReleaseArtifactEntry
        {
            FileName = "Evil-Setup.exe", Length = 10, Sha256 = new string('c', 64)
        });

        Assert.Equal(ManifestTrust.Rejected,
            ReleaseManifestSigning.Verify(manifest, publicKey, "0.9.22", Now).Trust);
    }

    [Fact]
    public void Verify_RejectsAManifestSignedByADifferentKey()
    {
        var (attackerPrivateKey, _) = KeyPair();
        var (_, trustedPublicKey) = KeyPair();
        var manifest = Manifest();
        ReleaseManifestSigning.Sign(manifest, attackerPrivateKey);

        Assert.Equal(ManifestTrust.Rejected,
            ReleaseManifestSigning.Verify(manifest, trustedPublicKey, "0.9.22", Now).Trust);
    }

    [Fact]
    public void Verify_RefusesToRollBackToAnOlderRelease()
    {
        var (privateKey, publicKey) = KeyPair();
        var manifest = Manifest(version: "0.9.10");
        ReleaseManifestSigning.Sign(manifest, privateKey);

        var result = ReleaseManifestSigning.Verify(manifest, publicKey, "0.9.22", Now);

        Assert.Equal(ManifestTrust.Rejected, result.Trust);
        Assert.Contains("Refusing to roll back", result.Detail);
    }

    [Fact]
    public void Verify_AcceptsTheSameVersionItIsAlreadyRunning()
    {
        var (privateKey, publicKey) = KeyPair();
        var manifest = Manifest(version: "0.9.22");
        ReleaseManifestSigning.Sign(manifest, privateKey);

        Assert.Equal(ManifestTrust.Trusted,
            ReleaseManifestSigning.Verify(manifest, publicKey, "0.9.22", Now).Trust);
    }

    [Fact]
    public void Verify_RejectsAnExpiredManifest()
    {
        var (privateKey, publicKey) = KeyPair();
        var manifest = Manifest();
        manifest.ExpiresAt = Now.AddDays(-1);
        ReleaseManifestSigning.Sign(manifest, privateKey);

        var result = ReleaseManifestSigning.Verify(manifest, publicKey, "0.9.22", Now);

        Assert.Equal(ManifestTrust.Rejected, result.Trust);
        Assert.Contains("expired", result.Detail);
    }

    [Fact]
    public void Verify_AcceptsAManifestThatHasNotExpiredYet()
    {
        var (privateKey, publicKey) = KeyPair();
        var manifest = Manifest();
        manifest.ExpiresAt = Now.AddDays(30);
        ReleaseManifestSigning.Sign(manifest, privateKey);

        Assert.Equal(ManifestTrust.Trusted,
            ReleaseManifestSigning.Verify(manifest, publicKey, "0.9.22", Now).Trust);
    }

    [Fact]
    public void Verify_RejectsAnExpiryThatWasMovedAfterSigning()
    {
        var (privateKey, publicKey) = KeyPair();
        var manifest = Manifest();
        manifest.ExpiresAt = Now.AddDays(-1);
        ReleaseManifestSigning.Sign(manifest, privateKey);

        manifest.ExpiresAt = Now.AddYears(10);

        Assert.Equal(ManifestTrust.Rejected,
            ReleaseManifestSigning.Verify(manifest, publicKey, "0.9.22", Now).Trust);
    }

    [Fact]
    public void Verify_ReportsUnsignedRatherThanTrustedWhenNoKeyIsCompiledIn()
    {
        var (privateKey, _) = KeyPair();
        var manifest = Manifest();
        ReleaseManifestSigning.Sign(manifest, privateKey);

        var result = ReleaseManifestSigning.Verify(manifest, publicKeyPem: "", "0.9.22", Now);

        Assert.Equal(ManifestTrust.Unsigned, result.Trust);
        Assert.True(result.CanApply);
    }

    [Fact]
    public void Verify_RejectsAnUnsignedManifestOnceAKeyIsCompiledIn()
    {
        var (_, publicKey) = KeyPair();

        var result = ReleaseManifestSigning.Verify(Manifest(), publicKey, "0.9.22", Now);

        Assert.Equal(ManifestTrust.Rejected, result.Trust);
        Assert.Contains("carries no signature", result.Detail);
    }

    [Fact]
    public void Verify_RejectsAnUnexpectedSignatureAlgorithm()
    {
        var (privateKey, publicKey) = KeyPair();
        var manifest = Manifest();
        ReleaseManifestSigning.Sign(manifest, privateKey);
        manifest.SignatureAlgorithm = "RSA-PKCS1-MD5";

        Assert.Equal(ManifestTrust.Rejected,
            ReleaseManifestSigning.Verify(manifest, publicKey, "0.9.22", Now).Trust);
    }

    [Fact]
    public void Sign_LeavesTheManifestVerifiableAfterARoundTripThroughJson()
    {
        var (privateKey, publicKey) = KeyPair();
        var manifest = Manifest();
        ReleaseManifestSigning.Sign(manifest, privateKey);

        var roundTripped = System.Text.Json.JsonSerializer.Deserialize<ReleaseArtifactManifest>(
            System.Text.Json.JsonSerializer.Serialize(manifest))!;

        Assert.Equal(ManifestTrust.Trusted,
            ReleaseManifestSigning.Verify(roundTripped, publicKey, "0.9.22", Now).Trust);
    }

    [Fact]
    public void ComputeSigningPayload_DoesNotDependOnArtifactOrdering()
    {
        var ordered = Manifest();
        var reversed = Manifest();
        reversed.Artifacts.Reverse();

        Assert.Equal(
            ReleaseManifestSigning.ComputeSigningPayload(ordered),
            ReleaseManifestSigning.ComputeSigningPayload(reversed));
    }

    [Fact]
    public void EvaluateManifestTrust_ReportsUnsignedUntilAKeyIsAdopted()
    {
        // The shipped build has no key yet, so the channel must report as unprotected rather than
        // rejecting every update and stranding installs.
        Assert.Equal(ManifestTrust.Unsigned, UpdateService.EvaluateManifestTrust(Manifest(), Now).Trust);
    }

    private static (string PrivateKeyPem, string PublicKeyPem) KeyPair()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (key.ExportECPrivateKeyPem(), key.ExportSubjectPublicKeyInfoPem());
    }

    private static ReleaseArtifactManifest Manifest(string version = "0.9.23") => new()
    {
        SchemaVersion = 1,
        AppVersion = version,
        GeneratedAt = Now,
        IsLocalTestBuild = false,
        SigningStatus = "UnsignedLocalTest",
        Artifacts =
        [
            new ReleaseArtifactEntry
            {
                FileName = $"PartitionPilot-{version}-Setup.exe", Length = 1234, Sha256 = new string('a', 64)
            },
            new ReleaseArtifactEntry
            {
                FileName = $"PartitionPilot-{version}-Portable.zip", Length = 5678, Sha256 = new string('d', 64)
            }
        ]
    };
}
