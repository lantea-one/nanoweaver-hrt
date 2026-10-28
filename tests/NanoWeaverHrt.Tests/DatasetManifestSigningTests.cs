using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.X509;
using NanoWeaverHrt.Models;
using NanoWeaverHrt.Security.Authentication;
using NanoWeaverHrt.Security.Cryptography;
using NanoWeaverHrt.Security.Models;
using NanoWeaverHrt.Security.Secrets;
using NanoWeaverHrt.Security.Signing;
using NanoWeaverHrt.Services;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Verification suite for Work Package P8: dataset manifest v2 signature fields, length-prefixed canonical
/// signing bytes, per-node Ed25519 dataset signer (<c>dataset_signing_ed25519.key</c>), Ed25519 and ML-DSA-65
/// signature verification, and automatic signing on dataset export.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class DatasetManifestSigningTests : IDisposable
{
    /// <summary>
    /// Isolated temporary root directory for this test instance.
    /// </summary>
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "weaver_ds_sign_" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Initializes a new instance of the <see cref="DatasetManifestSigningTests"/> class.
    /// </summary>
    public DatasetManifestSigningTests()
    {
        Directory.CreateDirectory(_tempRoot);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            try
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup of temp directory.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort cleanup of temp directory.
            }
        }
    }

    /// <summary>
    /// Verifies criteria 1, 2, 3, and 4: known-answer canonical byte encoding, field-boundary non-ambiguity,
    /// sensitivity to every signed field while ignoring <c>SignatureBase64</c>, and rejection of null fields or
    /// unpaired UTF-16 surrogates.
    /// </summary>
    [Fact]
    public void C1_To_C4_CanonicalizerKnownAnswerBoundariesAndValidation_MatchSpecification()
    {
        // Criterion 1: Known-answer test pinned as 250-char lowercase hex literal (125 bytes)
        const string expectedKatHex =
            "000000164e574852542d444154415345542d4d414e4946455354" +
            "00000002" +
            "0000000745643235353139" +
            "000000026b31" +
            "000000026162" +
            "0000000364c2b5" +
            "00000008642e7461722e677a" +
            "0000000000000102" +
            "0000001c323032362d31302d32305431383a30303a30302e303030303030305a" +
            "00000001" +
            "00000005612e78797a";

        DatasetManifest katManifestNullSig = new()
        {
            ManifestVersion = 2,
            SignatureAlgorithm = "Ed25519",
            SignerKeyId = "k1",
            Sha256Checksum = "ab",
            DatasetName = "d\u00b5",
            ArchiveFileName = "d.tar.gz",
            SizeInBytes = 258,
            TimestampUtc = "2026-10-20T18:00:00.0000000Z",
            IncludedFiles = ["a.xyz"],
            SignatureBase64 = null
        };

        DatasetManifest katManifestIgnoredSig = katManifestNullSig with
        {
            SignatureBase64 = "IGNORED"
        };

        byte[] katBytes1 = DatasetManifestCanonicalizer.GetSigningBytes(katManifestNullSig);
        byte[] katBytes2 = DatasetManifestCanonicalizer.GetSigningBytes(katManifestIgnoredSig);

        Assert.Equal(125, katBytes1.Length);
        Assert.Equal(expectedKatHex, Convert.ToHexString(katBytes1).ToLowerInvariant());
        Assert.Equal(expectedKatHex, Convert.ToHexString(katBytes2).ToLowerInvariant());

        // Criterion 2: Field-boundary tests
        byte[] boundaryA = DatasetManifestCanonicalizer.GetSigningBytes(katManifestNullSig with
        {
            DatasetName = "ab",
            ArchiveFileName = "c"
        });
        byte[] boundaryB = DatasetManifestCanonicalizer.GetSigningBytes(katManifestNullSig with
        {
            DatasetName = "a",
            ArchiveFileName = "bc"
        });
        Assert.NotEqual(boundaryA, boundaryB);

        byte[][] listVariants =
        [
            DatasetManifestCanonicalizer.GetSigningBytes(katManifestNullSig with { IncludedFiles = ["ab", "c"] }),
            DatasetManifestCanonicalizer.GetSigningBytes(katManifestNullSig with { IncludedFiles = ["a", "bc"] }),
            DatasetManifestCanonicalizer.GetSigningBytes(katManifestNullSig with { IncludedFiles = ["abc"] }),
            DatasetManifestCanonicalizer.GetSigningBytes(katManifestNullSig with { IncludedFiles = [] })
        ];
        for (int i = 0; i < listVariants.Length; i++)
        {
            for (int j = i + 1; j < listVariants.Length; j++)
            {
                Assert.NotEqual(listVariants[i], listVariants[j]);
            }
        }

        // Criterion 3: Each of the 9 signed inputs changed alone alters the bytes
        DatasetManifest[] singleFieldMutations =
        [
            katManifestNullSig with { ManifestVersion = 3 },
            katManifestNullSig with { SignatureAlgorithm = "MlDsa65" },
            katManifestNullSig with { SignerKeyId = "k2" },
            katManifestNullSig with { Sha256Checksum = "ac" },
            katManifestNullSig with { DatasetName = "d\u00b5x" },
            katManifestNullSig with { ArchiveFileName = "e.tar.gz" },
            katManifestNullSig with { SizeInBytes = 259 },
            katManifestNullSig with { TimestampUtc = "2026-10-20T18:00:01.0000000Z" },
            katManifestNullSig with { IncludedFiles = ["b.xyz"] }
        ];
        foreach (DatasetManifest mutation in singleFieldMutations)
        {
            Assert.NotEqual(katBytes1, DatasetManifestCanonicalizer.GetSigningBytes(mutation));
        }

        // Criterion 4: Null fields and lone surrogates throw ArgumentException
        Assert.Throws<ArgumentException>(() => DatasetManifestCanonicalizer.GetSigningBytes(null!));
        Assert.Throws<ArgumentException>(() => DatasetManifestCanonicalizer.GetSigningBytes(katManifestNullSig with { ManifestVersion = null }));
        Assert.Throws<ArgumentException>(() => DatasetManifestCanonicalizer.GetSigningBytes(katManifestNullSig with { SignatureAlgorithm = null }));
        Assert.Throws<ArgumentException>(() => DatasetManifestCanonicalizer.GetSigningBytes(katManifestNullSig with { SignerKeyId = null }));
        Assert.Throws<ArgumentException>(() => DatasetManifestCanonicalizer.GetSigningBytes(katManifestNullSig with { Sha256Checksum = null! }));
        Assert.Throws<ArgumentException>(() => DatasetManifestCanonicalizer.GetSigningBytes(katManifestNullSig with { DatasetName = null! }));
        Assert.Throws<ArgumentException>(() => DatasetManifestCanonicalizer.GetSigningBytes(katManifestNullSig with { ArchiveFileName = null! }));
        Assert.Throws<ArgumentException>(() => DatasetManifestCanonicalizer.GetSigningBytes(katManifestNullSig with { TimestampUtc = null! }));
        Assert.Throws<ArgumentException>(() => DatasetManifestCanonicalizer.GetSigningBytes(katManifestNullSig with { IncludedFiles = null! }));
        Assert.Throws<ArgumentException>(() => DatasetManifestCanonicalizer.GetSigningBytes(katManifestNullSig with { IncludedFiles = [null!] }));
        Assert.Throws<ArgumentException>(() => DatasetManifestCanonicalizer.GetSigningBytes(katManifestNullSig with { DatasetName = "bad\ud800name" }));
    }

    /// <summary>
    /// Verifies criteria 6 and 7: <see cref="DatasetManifestSignature.GetState"/> classification across unsigned,
    /// signed, and all malformed cases, <see cref="DatasetManifestSignature.TryParseAlgorithm"/>, and
    /// <see cref="DatasetManifestSignature.ComputeKeyId"/>.
    /// </summary>
    [Fact]
    public void C6_C7_SignatureStateClassificationAndAlgorithmParsing_BehaveAccurately()
    {
        Assert.Throws<ArgumentNullException>(() => DatasetManifestSignature.GetState(null!));

        string validKeyId = new('a', 64);
        DatasetManifest baseUnsigned = CreateSampleManifest();

        // Criterion 6: Unsigned even when ManifestVersion == 2
        Assert.Equal(DatasetManifestSignatureState.Unsigned, DatasetManifestSignature.GetState(baseUnsigned));
        Assert.Equal(DatasetManifestSignatureState.Unsigned, DatasetManifestSignature.GetState(baseUnsigned with { ManifestVersion = 2 }));

        DatasetManifest validSigned = baseUnsigned with
        {
            ManifestVersion = 2,
            SignatureAlgorithm = "Ed25519",
            SignerKeyId = validKeyId,
            SignatureBase64 = "AA=="
        };
        Assert.Equal(DatasetManifestSignatureState.Signed, DatasetManifestSignature.GetState(validSigned));

        // Malformed cases:
        // - one field present
        Assert.Equal(DatasetManifestSignatureState.Malformed, DatasetManifestSignature.GetState(baseUnsigned with { SignatureAlgorithm = "Ed25519" }));
        Assert.Equal(DatasetManifestSignatureState.Malformed, DatasetManifestSignature.GetState(baseUnsigned with { SignerKeyId = validKeyId }));
        Assert.Equal(DatasetManifestSignatureState.Malformed, DatasetManifestSignature.GetState(baseUnsigned with { SignatureBase64 = "AA==" }));
        // - two fields present
        Assert.Equal(DatasetManifestSignatureState.Malformed, DatasetManifestSignature.GetState(baseUnsigned with { SignatureAlgorithm = "Ed25519", SignerKeyId = validKeyId }));
        Assert.Equal(DatasetManifestSignatureState.Malformed, DatasetManifestSignature.GetState(baseUnsigned with { SignatureAlgorithm = "Ed25519", SignatureBase64 = "AA==" }));
        Assert.Equal(DatasetManifestSignatureState.Malformed, DatasetManifestSignature.GetState(baseUnsigned with { SignerKeyId = validKeyId, SignatureBase64 = "AA==" }));
        // - empty string / whitespace
        Assert.Equal(DatasetManifestSignatureState.Malformed, DatasetManifestSignature.GetState(validSigned with { SignatureAlgorithm = "" }));
        Assert.Equal(DatasetManifestSignatureState.Malformed, DatasetManifestSignature.GetState(validSigned with { SignatureAlgorithm = "   " }));
        Assert.Equal(DatasetManifestSignatureState.Malformed, DatasetManifestSignature.GetState(validSigned with { SignerKeyId = "   " }));
        Assert.Equal(DatasetManifestSignatureState.Malformed, DatasetManifestSignature.GetState(validSigned with { SignatureBase64 = "   " }));
        // - algorithm "ML-DSA-65" or "ed25519" (wrong case)
        Assert.Equal(DatasetManifestSignatureState.Malformed, DatasetManifestSignature.GetState(validSigned with { SignatureAlgorithm = "ML-DSA-65" }));
        Assert.Equal(DatasetManifestSignatureState.Malformed, DatasetManifestSignature.GetState(validSigned with { SignatureAlgorithm = "ed25519" }));
        // - 63-char key id, 64-char with uppercase hex digit, 64-char with non-hex character
        Assert.Equal(DatasetManifestSignatureState.Malformed, DatasetManifestSignature.GetState(validSigned with { SignerKeyId = new string('a', 63) }));
        Assert.Equal(DatasetManifestSignatureState.Malformed, DatasetManifestSignature.GetState(validSigned with { SignerKeyId = new string('a', 63) + "F" }));
        Assert.Equal(DatasetManifestSignatureState.Malformed, DatasetManifestSignature.GetState(validSigned with { SignerKeyId = new string('a', 63) + "g" }));
        Assert.Equal(DatasetManifestSignatureState.Malformed, DatasetManifestSignature.GetState(validSigned with { SignerKeyId = new string('a', 63) + "/" }));
        // - version null, 1, 3
        Assert.Equal(DatasetManifestSignatureState.Malformed, DatasetManifestSignature.GetState(validSigned with { ManifestVersion = null }));
        Assert.Equal(DatasetManifestSignatureState.Malformed, DatasetManifestSignature.GetState(validSigned with { ManifestVersion = 1 }));
        Assert.Equal(DatasetManifestSignatureState.Malformed, DatasetManifestSignature.GetState(validSigned with { ManifestVersion = 3 }));

        // Criterion 7: TryParseAlgorithm and ComputeKeyId
        Assert.True(DatasetManifestSignature.TryParseAlgorithm("Ed25519", out KeyAlgorithm alg1));
        Assert.Equal(KeyAlgorithm.Ed25519, alg1);
        Assert.True(DatasetManifestSignature.TryParseAlgorithm("MlDsa65", out KeyAlgorithm alg2));
        Assert.Equal(KeyAlgorithm.MlDsa65, alg2);
        Assert.False(DatasetManifestSignature.TryParseAlgorithm(null, out _));
        Assert.False(DatasetManifestSignature.TryParseAlgorithm("", out _));
        Assert.False(DatasetManifestSignature.TryParseAlgorithm("ed25519", out _));
        Assert.False(DatasetManifestSignature.TryParseAlgorithm("ML-DSA-65", out _));

        byte[] randomDer = RandomNumberGenerator.GetBytes(64);
        string expectedKeyId = Convert.ToHexString(SHA256.HashData(randomDer)).ToLowerInvariant();
        Assert.Equal(expectedKeyId, DatasetManifestSignature.ComputeKeyId(randomDer));
    }

    /// <summary>
    /// Verifies criteria 8, 9, 10, 11, and 12: RFC 8032 §7.1 TEST 1 wiring check, regression-pinned signature on a
    /// fixed manifest, <see cref="NodeDatasetSigner.Sign"/> metadata population, key file lifecycle and isolation,
    /// and private seed encapsulation.
    /// </summary>
    [Fact]
    public void C8_To_C12_NodeDatasetSignerRfc8032LifecycleAndSeedIsolation_Succeed()
    {
        Assert.Throws<ArgumentNullException>(() => new NodeDatasetSigner(null!));

        // Criterion 8: RFC 8032 §7.1 TEST 1 wiring check
        string rfcKeysDir = Path.Combine(_tempRoot, "rfc8032_keys");
        Directory.CreateDirectory(rfcKeysDir);
        byte[] rfcSeed = Convert.FromHexString("9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60");
        File.WriteAllBytes(Path.Combine(rfcKeysDir, NodeDatasetSigner.KeyFileName), rfcSeed);

        NodeSecretStore rfcStore = CreateStoreForKeysDir(rfcKeysDir);
        NodeDatasetSigner rfcSigner = new(rfcStore);

        Assert.Equal(KeyAlgorithm.Ed25519, rfcSigner.Algorithm);
        Assert.Equal(NodeSecretOrigin.ExistingFile, rfcSigner.KeyOrigin);

        byte[] spkiDer = Convert.FromBase64String(rfcSigner.PublicKeyBase64);
        Assert.Equal(44, spkiDer.Length);
        Assert.Equal("302a300506032b6570032100", Convert.ToHexStringLower(spkiDer.AsSpan(0, 12)));
        Assert.Equal(
            "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a",
            Convert.ToHexStringLower(spkiDer.AsSpan(12, 32)));

        byte[] emptyMsgSig = rfcSigner.SignCanonicalBytes(ReadOnlySpan<byte>.Empty);
        Assert.Equal(
            "e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b",
            Convert.ToHexStringLower(emptyMsgSig));

        // Criterion 9 & 10: KeyId equals ComputeKeyId(spkiDer); Sign populates v2 metadata and matches pinned SignatureBase64
        Assert.Equal(DatasetManifestSignature.ComputeKeyId(spkiDer), rfcSigner.KeyId);

        DatasetManifest inputManifest = CreateSampleManifest() with
        {
            SignatureBase64 = "PRE_EXISTING_SIGNATURE_TO_REPLACE"
        };
        DatasetManifest signedManifest = rfcSigner.Sign(inputManifest);

        Assert.Equal(2, signedManifest.ManifestVersion);
        Assert.Equal("Ed25519", signedManifest.SignatureAlgorithm);
        Assert.Equal(rfcSigner.KeyId, signedManifest.SignerKeyId);
        Assert.NotNull(signedManifest.SignatureBase64);
        Assert.NotEqual("PRE_EXISTING_SIGNATURE_TO_REPLACE", signedManifest.SignatureBase64);
        Assert.Equal(inputManifest.DatasetName, signedManifest.DatasetName);
        Assert.Equal(inputManifest.TimestampUtc, signedManifest.TimestampUtc);
        Assert.Equal(inputManifest.ArchiveFileName, signedManifest.ArchiveFileName);
        Assert.Equal(inputManifest.Sha256Checksum, signedManifest.Sha256Checksum);
        Assert.Equal(inputManifest.SizeInBytes, signedManifest.SizeInBytes);
        Assert.Equal(inputManifest.IncludedFiles, signedManifest.IncludedFiles);
        Assert.Equal(DatasetManifestSignatureState.Signed, DatasetManifestSignature.GetState(signedManifest));

        // Regression pin derived once from the implementation after criteria 1 and 8 passed (per Criterion 9).
        Assert.Equal(PinnedRfcSeedManifestSignatureBase64, signedManifest.SignatureBase64);

        // Criterion 11: Key file lifecycle and token/PHI key isolation
        string lifecycleKeysDir = Path.Combine(_tempRoot, "lifecycle_keys");
        NodeSecretStore lifecycleStore = CreateStoreForKeysDir(lifecycleKeysDir);
        string tokenPath = Path.Combine(lifecycleKeysDir, "token_signing.key");
        string phiPath = Path.Combine(lifecycleKeysDir, "phi_master.key");
        string dsKeyPath = Path.Combine(lifecycleKeysDir, NodeDatasetSigner.KeyFileName);

        byte[] tokenBefore = File.ReadAllBytes(tokenPath);
        byte[] phiBefore = File.ReadAllBytes(phiPath);

        NodeDatasetSigner genSigner = new(lifecycleStore);
        Assert.Equal(NodeSecretOrigin.GeneratedFile, genSigner.KeyOrigin);
        Assert.Equal(32, new FileInfo(dsKeyPath).Length);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(dsKeyPath));

        NodeDatasetSigner reloadSigner = new(lifecycleStore);
        Assert.Equal(NodeSecretOrigin.ExistingFile, reloadSigner.KeyOrigin);
        Assert.Equal(genSigner.KeyId, reloadSigner.KeyId);
        Assert.Equal(tokenBefore, File.ReadAllBytes(tokenPath));
        Assert.Equal(phiBefore, File.ReadAllBytes(phiPath));

        string badLenKeysDir = Path.Combine(_tempRoot, "bad_31_keys");
        NodeSecretStore badLenStore = CreateStoreForKeysDir(badLenKeysDir);
        File.WriteAllBytes(Path.Combine(badLenKeysDir, NodeDatasetSigner.KeyFileName), new byte[31]);
        Assert.Throws<NodeSecretProvisioningException>(() => new NodeDatasetSigner(badLenStore));

        // Criterion 12: Seed never leaves the signer
        Type[] forbiddenTypes =
        [
            typeof(byte[]),
            typeof(ReadOnlyMemory<byte>),
            typeof(Memory<byte>)
        ];
        FieldInfo[] exposedFields = typeof(NodeDatasetSigner)
            .GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(f => !f.IsPrivate && (forbiddenTypes.Contains(f.FieldType) || typeof(AsymmetricKeyParameter).IsAssignableFrom(f.FieldType)))
            .ToArray();
        Assert.Empty(exposedFields);

        PropertyInfo[] exposedProps = typeof(NodeDatasetSigner)
            .GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(p =>
            {
                MethodInfo? getter = p.GetGetMethod(nonPublic: true);
                return getter is not null && !getter.IsPrivate && (forbiddenTypes.Contains(p.PropertyType) || typeof(AsymmetricKeyParameter).IsAssignableFrom(p.PropertyType));
            })
            .ToArray();
        Assert.Empty(exposedProps);

        string seedHex = Convert.ToHexStringLower(rfcSeed);
        string seedBase64 = Convert.ToBase64String(rfcSeed);
        string signedJson = JsonSerializer.Serialize(signedManifest);

        Assert.DoesNotContain(seedHex, Convert.ToHexStringLower(spkiDer), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(seedHex, rfcSigner.KeyId, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(seedHex, signedJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(seedBase64, signedJson, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies criteria 13, 14, and 15: <see cref="DatasetManifestSignatureVerifier.Verify"/> positive paths for
    /// Ed25519 and ML-DSA-65, and all negative / malformed / algorithm-confusion cases without throwing.
    /// </summary>
    [Fact]
    public void C13_To_C15_SignatureVerifierEd25519MlDsa65AndNegativeCases_BehaveAccurately()
    {
        Assert.Throws<ArgumentNullException>(() => new DatasetManifestSignatureVerifier(null!));

        PostQuantumCryptoProvider pqc = new();
        DatasetManifestSignatureVerifier verifier = new(pqc);
        Assert.Throws<ArgumentNullException>(() => verifier.Verify(null!, KeyAlgorithm.Ed25519, "AA=="));

        // Criterion 13: Positive Ed25519 verification
        NodeSecretStore store = CreateStoreForKeysDir(Path.Combine(_tempRoot, "verifier_keys"));
        NodeDatasetSigner edSigner = new(store);
        DatasetManifest edSigned = edSigner.Sign(CreateSampleManifest());
        Assert.True(verifier.Verify(edSigned, edSigner.Algorithm, edSigner.PublicKeyBase64));

        // Criterion 14: Positive ML-DSA-65 verification
        PqcKeyMaterial mldsaKey = pqc.GenerateSigningKeyPair("temp");
        byte[] mldsaSpkiDer = Convert.FromBase64String(mldsaKey.PublicKeyBase64);
        string mldsaKeyId = DatasetManifestSignature.ComputeKeyId(mldsaSpkiDer);

        DatasetManifest mldsaPreSigned = CreateSampleManifest() with
        {
            ManifestVersion = 2,
            SignatureAlgorithm = "MlDsa65",
            SignerKeyId = mldsaKeyId
        };
        byte[] mldsaCanonical = DatasetManifestCanonicalizer.GetSigningBytes(mldsaPreSigned);
        PqcSignature mldsaSig = pqc.SignData(mldsaCanonical, mldsaKey.PrivateKeyBase64!, mldsaKeyId);
        DatasetManifest mldsaSigned = mldsaPreSigned with
        {
            SignatureBase64 = mldsaSig.SignatureBase64
        };
        Assert.True(verifier.Verify(mldsaSigned, KeyAlgorithm.MlDsa65, mldsaKey.PublicKeyBase64));

        // Criterion 15: Negative cases all return false without throwing
        // - one flipped signature byte
        byte[] rawEdSig = Convert.FromBase64String(edSigned.SignatureBase64!);
        rawEdSig[0] ^= 0x01;
        Assert.False(verifier.Verify(edSigned with { SignatureBase64 = Convert.ToBase64String(rawEdSig) }, KeyAlgorithm.Ed25519, edSigner.PublicKeyBase64));

        // - each of the nine signed fields altered after signing
        Assert.False(verifier.Verify(edSigned with { ManifestVersion = 3 }, KeyAlgorithm.Ed25519, edSigner.PublicKeyBase64));
        Assert.False(verifier.Verify(edSigned with { SignatureAlgorithm = "MlDsa65" }, KeyAlgorithm.Ed25519, edSigner.PublicKeyBase64));
        Assert.False(verifier.Verify(edSigned with { SignerKeyId = new string('0', 64) }, KeyAlgorithm.Ed25519, edSigner.PublicKeyBase64));
        Assert.False(verifier.Verify(edSigned with { Sha256Checksum = new string('f', 64) }, KeyAlgorithm.Ed25519, edSigner.PublicKeyBase64));
        Assert.False(verifier.Verify(edSigned with { DatasetName = "tampered-name" }, KeyAlgorithm.Ed25519, edSigner.PublicKeyBase64));
        Assert.False(verifier.Verify(edSigned with { ArchiveFileName = "tampered.tar.gz" }, KeyAlgorithm.Ed25519, edSigner.PublicKeyBase64));
        Assert.False(verifier.Verify(edSigned with { SizeInBytes = edSigned.SizeInBytes + 1 }, KeyAlgorithm.Ed25519, edSigner.PublicKeyBase64));
        Assert.False(verifier.Verify(edSigned with { TimestampUtc = "2026-10-20T18:00:09.0000000Z" }, KeyAlgorithm.Ed25519, edSigner.PublicKeyBase64));
        Assert.False(verifier.Verify(edSigned with { IncludedFiles = ["other.xyz"] }, KeyAlgorithm.Ed25519, edSigner.PublicKeyBase64));

        // - trusted algorithm differs from manifest's
        Assert.False(verifier.Verify(edSigned, KeyAlgorithm.MlDsa65, edSigner.PublicKeyBase64));
        Assert.False(verifier.Verify(mldsaSigned, KeyAlgorithm.Ed25519, mldsaKey.PublicKeyBase64));

        // - SignerKeyId does not match trusted key
        NodeDatasetSigner otherEdSigner = new(CreateStoreForKeysDir(Path.Combine(_tempRoot, "other_ed_keys")));
        Assert.False(verifier.Verify(edSigned, KeyAlgorithm.Ed25519, otherEdSigner.PublicKeyBase64));

        // - trusted key is whitespace, not valid Base64, or valid Base64 but not an SPKI
        Assert.False(verifier.Verify(edSigned, KeyAlgorithm.Ed25519, "   "));
        Assert.False(verifier.Verify(edSigned, KeyAlgorithm.Ed25519, "!!!not-base64!!!"));
        byte[] garbageBytes = [0x01, 0x02, 0x03, 0x04];
        string garbageKeyId = DatasetManifestSignature.ComputeKeyId(garbageBytes);
        Assert.False(verifier.Verify(edSigned with { SignerKeyId = garbageKeyId }, KeyAlgorithm.Ed25519, Convert.ToBase64String(garbageBytes)));

        // - Ed25519 key supplied as MlDsa65 and the reverse (with matching SignerKeyId and SignatureAlgorithm)
        Assert.False(verifier.Verify(
            edSigned with { SignatureAlgorithm = "MlDsa65", SignerKeyId = edSigner.KeyId },
            KeyAlgorithm.MlDsa65,
            edSigner.PublicKeyBase64));
        Assert.False(verifier.Verify(
            mldsaSigned with { SignatureAlgorithm = "Ed25519", SignerKeyId = mldsaKeyId },
            KeyAlgorithm.Ed25519,
            mldsaKey.PublicKeyBase64));

        // - SignatureBase64 not valid Base64
        Assert.False(verifier.Verify(edSigned with { SignatureBase64 = "%%%not-base64%%%" }, KeyAlgorithm.Ed25519, edSigner.PublicKeyBase64));

        // - Unsigned and Malformed manifests
        Assert.False(verifier.Verify(CreateSampleManifest(), KeyAlgorithm.Ed25519, edSigner.PublicKeyBase64));
        Assert.False(verifier.Verify(edSigned with { ManifestVersion = 1 }, KeyAlgorithm.Ed25519, edSigner.PublicKeyBase64));

        // - Signed field holding a lone surrogate (canonicalizer throws ArgumentException, caught by Verify)
        Assert.False(verifier.Verify(edSigned with { DatasetName = "lone\ud800surrogate" }, KeyAlgorithm.Ed25519, edSigner.PublicKeyBase64));
    }

    /// <summary>
    /// Verifies criteria 16, 17, 18, 19, and 20: <see cref="DatasetTransferService"/> export with and without a
    /// signer, legacy and signed JSON round-trip compatibility, host-level <c>POST /api/datasets/export</c> and
    /// startup log line, and integrity-only <c>ImportDatasetAsync</c> behavior.
    /// </summary>
    [Fact]
    public async Task C16_To_C20_ImportDatasetAsync_IsIntegrityOnly_AndIgnoresSignatures()
    {
        string svcRoot = Path.Combine(_tempRoot, "svc_transfer");
        string incomingDir = Path.Combine(svcRoot, "incoming");
        string exportWithSignerDir = Path.Combine(svcRoot, "exports_signed");
        string exportUnsignedDir = Path.Combine(svcRoot, "exports_unsigned");
        string keysDir = Path.Combine(svcRoot, "node-keys");
        Directory.CreateDirectory(incomingDir);
        Directory.CreateDirectory(exportWithSignerDir);
        Directory.CreateDirectory(exportUnsignedDir);
        Directory.CreateDirectory(keysDir);
        await File.WriteAllTextAsync(Path.Combine(incomingDir, "sample.xyz"), "2\ncomment\nC 0 0 0\nO 1 1 1\n");

        NodeSecretStore store = CreateStoreForKeysDir(keysDir);
        NodeDatasetSigner signer = new(store);
        DatasetManifestSignatureVerifier verifier = new(new PostQuantumCryptoProvider());

        // Criterion 16: Service built WITH a signer exports signed manifest on disk and in return value
        IConfiguration signedConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = incomingDir,
                ["TRAJECTORY_EXPORT_DIR"] = exportWithSignerDir
            })
            .Build();
        DatasetTransferService signedService = new(signedConfig, NullLogger<DatasetTransferService>.Instance, signer);
        DatasetManifest exportedSigned = await signedService.ExportDatasetAsync("signed_bundle");

        Assert.Equal(DatasetManifestSignatureState.Signed, DatasetManifestSignature.GetState(exportedSigned));
        Assert.True(verifier.Verify(exportedSigned, signer.Algorithm, signer.PublicKeyBase64));

        string diskSignedJson = await File.ReadAllTextAsync(Path.Combine(exportWithSignerDir, "signed_bundle.manifest.json"));
        DatasetManifest diskSigned = JsonSerializer.Deserialize<DatasetManifest>(diskSignedJson)!;
        Assert.Equal(exportedSigned.SignatureBase64, diskSigned.SignatureBase64);
        Assert.Equal(exportedSigned.SignerKeyId, diskSigned.SignerKeyId);
        Assert.True(verifier.Verify(diskSigned, signer.Algorithm, signer.PublicKeyBase64));

        // Criterion 17: Service built WITHOUT a signer omits all 4 signature properties from disk JSON
        IConfiguration unsignedConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TRAJECTORY_INCOMING_DIR"] = incomingDir,
                ["TRAJECTORY_EXPORT_DIR"] = exportUnsignedDir
            })
            .Build();
        DatasetTransferService unsignedService = new(unsignedConfig, NullLogger<DatasetTransferService>.Instance);
        DatasetManifest exportedUnsigned = await unsignedService.ExportDatasetAsync("unsigned_bundle");
        Assert.Equal(DatasetManifestSignatureState.Unsigned, DatasetManifestSignature.GetState(exportedUnsigned));

        string diskUnsignedJson = await File.ReadAllTextAsync(Path.Combine(exportUnsignedDir, "unsigned_bundle.manifest.json"));
        Assert.DoesNotContain("manifestVersion", diskUnsignedJson, StringComparison.Ordinal);
        Assert.DoesNotContain("signatureAlgorithm", diskUnsignedJson, StringComparison.Ordinal);
        Assert.DoesNotContain("signerKeyId", diskUnsignedJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"signature\"", diskUnsignedJson, StringComparison.Ordinal);

        // Criterion 18: Legacy 6-property JSON deserializes as Unsigned; signed JSON round-trips cleanly
        DatasetManifest deserializedLegacy = JsonSerializer.Deserialize<DatasetManifest>(diskUnsignedJson)!;
        Assert.Null(deserializedLegacy.ManifestVersion);
        Assert.Null(deserializedLegacy.SignatureAlgorithm);
        Assert.Null(deserializedLegacy.SignerKeyId);
        Assert.Null(deserializedLegacy.SignatureBase64);
        Assert.Equal(DatasetManifestSignatureState.Unsigned, DatasetManifestSignature.GetState(deserializedLegacy));

        string roundTripSignedJson = JsonSerializer.Serialize(exportedSigned);
        DatasetManifest roundTripSigned = JsonSerializer.Deserialize<DatasetManifest>(roundTripSignedJson)!;
        Assert.True(verifier.Verify(roundTripSigned, signer.Algorithm, signer.PublicKeyBase64));

        // Criterion 20: ImportDatasetAsync is integrity-only and accepts both valid and corrupted signatures when SHA-256 matches
        string archivePath = Path.Combine(exportWithSignerDir, exportedSigned.ArchiveFileName);
        await using (FileStream validStream = File.OpenRead(archivePath))
        {
            Assert.True(await signedService.ImportDatasetAsync(validStream, exportedSigned));
        }

        DatasetManifest corruptedSigManifest = exportedSigned with
        {
            SignatureBase64 = Convert.ToBase64String(new byte[64])
        };
        await using (FileStream corruptedSigStream = File.OpenRead(archivePath))
        {
            Assert.True(await signedService.ImportDatasetAsync(corruptedSigStream, corruptedSigManifest));
        }

        // Criterion 19: Host-level POST /api/datasets/export returns a signed manifest verified by host's INodeDatasetSigner
        string hostRoot = Path.Combine(_tempRoot, "host_export");
        string hostShared = Path.Combine(hostRoot, "shared-data");
        string hostExport = Path.Combine(hostRoot, "exports");
        string hostParquet = Path.Combine(hostRoot, "parquet");
        string hostKeys = Path.Combine(hostRoot, "node-keys");
        Directory.CreateDirectory(hostShared);
        Directory.CreateDirectory(hostExport);
        Directory.CreateDirectory(hostParquet);
        Directory.CreateDirectory(hostKeys);
        TestFixturesHelper.EnsureTestFixtures(hostShared);

        ConcurrentQueue<string> hostLogs = new();
        using WebApplicationFactory<Program> baseFactory = new();
        using WebApplicationFactory<Program> factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TRAJECTORY_INCOMING_DIR"] = hostShared,
                    ["TRAJECTORY_EXPORT_DIR"] = hostExport,
                    ["TRAJECTORY_PARQUET_DIR"] = hostParquet,
                    ["WEAVER_NODE_KEYS_DIR"] = hostKeys,
                    ["POSTGRES_CONNECTION_STRING"] = string.Empty
                });
            });
            builder.ConfigureLogging(logging =>
            {
                logging.AddProvider(new CapturingLoggerProvider(hostLogs));
            });
        });
        using HttpClient client = factory.CreateClient();
        TestFixturesHelper.ConfigureAuthenticatedClient(client, factory);

        HttpResponseMessage exportResp = await client.PostAsJsonAsync("/api/datasets/export", new ExportRequest
        {
            DatasetName = "host_signed_export"
        });
        Assert.Equal(HttpStatusCode.OK, exportResp.StatusCode);
        DatasetManifest hostManifest = (await exportResp.Content.ReadFromJsonAsync<DatasetManifest>())!;

        INodeDatasetSigner hostSigner = factory.Services.GetRequiredService<INodeDatasetSigner>();
        Assert.True(verifier.Verify(hostManifest, hostSigner.Algorithm, hostSigner.PublicKeyBase64));

        string expectedLog = $"Dataset signing key ready. Origin: GeneratedFile. Fingerprint: {hostSigner.KeyId}.";
        string[] matchingLogs = hostLogs.Where(l => l.Contains("Dataset signing key ready.", StringComparison.Ordinal)).ToArray();
        Assert.Single(matchingLogs);
        Assert.Contains(expectedLog, matchingLogs[0], StringComparison.Ordinal);
        Assert.DoesNotContain("/", matchingLogs[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// Pinned Base64 Ed25519 signature over <see cref="CreateSampleManifest"/> signed with RFC 8032 TEST 1 seed,
    /// derived once from the implementation after criteria 1 and 8 passed (per Criterion 9).
    /// </summary>
    private const string PinnedRfcSeedManifestSignatureBase64 = "qCFc7GNE8Owwm6cOY1U0q1WX4QJML7AiLDe0x3JMajF+mBtJIC6gZFP/Og4VViRWuVSfEYd4J0BgyU1idVBJBg==";

    /// <summary>
    /// Creates a deterministic unsigned sample <see cref="DatasetManifest"/> with no wall-clock dependency.
    /// </summary>
    /// <returns>A populated unsigned <see cref="DatasetManifest"/>.</returns>
    private static DatasetManifest CreateSampleManifest() => new()
    {
        DatasetName = "dna-origami-tile-500k",
        TimestampUtc = "2026-10-20T18:00:00.0000000Z",
        ArchiveFileName = "dna-origami-tile-500k.tar.gz",
        Sha256Checksum = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
        SizeInBytes = 14850200,
        IncludedFiles = ["trajectory.xyz", "simulation.in"]
    };

    /// <summary>
    /// Creates a <see cref="NodeSecretStore"/> bound to a specific node-keys directory.
    /// </summary>
    /// <param name="keysDir">Absolute path to the node-keys directory.</param>
    /// <returns>The initialized <see cref="NodeSecretStore"/>.</returns>
    private static NodeSecretStore CreateStoreForKeysDir(string keysDir)
    {
        Directory.CreateDirectory(keysDir);
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WEAVER_NODE_KEYS_DIR"] = keysDir
            })
            .Build();
        return new NodeSecretStore(config);
    }

    /// <summary>
    /// Logger provider that records every formatted log line into a thread-safe queue.
    /// </summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        /// <summary>
        /// Target log message queue.
        /// </summary>
        private readonly ConcurrentQueue<string> _sink;

        /// <summary>
        /// Initializes a new instance of the <see cref="CapturingLoggerProvider"/> class.
        /// </summary>
        /// <param name="sink">Queue receiving formatted log lines.</param>
        public CapturingLoggerProvider(ConcurrentQueue<string> sink) => _sink = sink;

        /// <inheritdoc/>
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_sink);

        /// <inheritdoc/>
        public void Dispose()
        {
        }
    }

    /// <summary>
    /// Minimal <see cref="ILogger"/> implementation that appends formatted log lines to a queue.
    /// </summary>
    private sealed class CapturingLogger : ILogger
    {
        /// <summary>
        /// Target log message queue.
        /// </summary>
        private readonly ConcurrentQueue<string> _sink;

        /// <summary>
        /// Initializes a new instance of the <see cref="CapturingLogger"/> class.
        /// </summary>
        /// <param name="sink">Queue receiving formatted log lines.</param>
        public CapturingLogger(ConcurrentQueue<string> sink) => _sink = sink;

        /// <inheritdoc/>
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        /// <inheritdoc/>
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc/>
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            _sink.Enqueue(formatter(state, exception));
    }
}
