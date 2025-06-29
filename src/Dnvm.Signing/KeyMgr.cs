
using System.Security.Cryptography;
using Azure.Core;
using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;

namespace Dnvm.Signing;

/// <summary>
/// Handles key management operations for Dnvm signing. Dnvm uses two sets of
/// key pairs:
/// 1. A root key pair stored in Azure Key Vault, used to sign release keys.
/// 2. Release keys generated on-demand, which are signed by the root key
///    and used to sign release files.
///
/// To generate a new release key pair, call <see cref="GenerateReleaseKey"/>.
/// To sign a public key with the root key, use <see cref="SignReleaseKey"/>.
/// To verify a release key signature, use <see cref="VerifyReleaseKey"/>.
/// To sign a release file, use <see cref="SignRelease"/>.
/// To verify a release file, use <see cref="VerifyRelease"/>.
/// </summary>
public static class KeyMgr
{
    public const string RootKeyVaultUrl = "https://dnvm-root.vault.azure.net/";
    public const string RootKeyName = "dnvm-root";

    /// <summary>
    /// Create a new signing key pair.
    /// </summary>
    public static (string PrivateKey, string PublicKey) GenerateReleaseKey()
    {
        // Generate a new signing key pair
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string publicKey = ecdsa.ExportSubjectPublicKeyInfoPem();
        string privateKey = ecdsa.ExportECPrivateKeyPem();
        return (privateKey, publicKey);
    }

    /// <summary>
    /// Signs a public key using the root key from Azure Key Vault. Returns the signature.
    /// </summary>
    public static async Task<byte[]> SignReleaseKey(TokenCredential cred, byte[] pubKey)
    {
        // Fetch the root key from Azure Key Vault
        var keyClient = new KeyClient(new Uri(RootKeyVaultUrl), cred);
        KeyVaultKey key = await keyClient.GetKeyAsync(RootKeyName);

        // Use CryptoClient to sign the public key file's bytes
        var cryptoClient = new CryptographyClient(key.Id, cred);
        // Use SHA256 for P-256, SHA384 for P-384, SHA512 for P-521
        var curveName = key.Key.CurveName;
        if (curveName is null)
            throw new InvalidOperationException("CurveName is null on the root key");
        SignatureAlgorithm sigAlg;
        byte[] digest;
        if (curveName == KeyCurveName.P256)
        {
            sigAlg = SignatureAlgorithm.ES256;
            using var hasher = SHA256.Create();
            digest = hasher.ComputeHash(pubKey);
        }
        else if (curveName == KeyCurveName.P384)
        {
            sigAlg = SignatureAlgorithm.ES384;
            using var hasher = SHA384.Create();
            digest = hasher.ComputeHash(pubKey);
        }
        else if (curveName == KeyCurveName.P521)
        {
            sigAlg = SignatureAlgorithm.ES512;
            using var hasher = SHA512.Create();
            digest = hasher.ComputeHash(pubKey);
        }
        else
        {
            throw new NotSupportedException($"Curve {curveName} not supported for signing");
        }
        // Sign the digest
        var signResult = cryptoClient.Sign(sigAlg, digest);
        return signResult.Signature;
    }

    /// <summary>
    /// Represents the root public key used for signing "release keys."
    /// </summary>
    public sealed class RootPubKey : IEquatable<RootPubKey>, IDisposable
    {
        internal ECDsa ECDsa { get; }

        internal RootPubKey(ECDsa ec)
        {
            ECDsa = ec;
        }

        public byte[] GetPublicKey()
        {
            // Export the public key in SubjectPublicKeyInfo format
            return ECDsa.ExportSubjectPublicKeyInfo();
        }

        public string ExportToPem()
        {
            return ECDsa.ExportSubjectPublicKeyInfoPem();
        }

        public void Dispose()
        {
            ECDsa?.Dispose();
        }

        public bool Equals(RootPubKey? other)
        {
            var thisPub = ECDsa.ExportSubjectPublicKeyInfo();
            var otherPub = other?.ECDsa.ExportSubjectPublicKeyInfo();
            return otherPub != null && thisPub.SequenceEqual(otherPub);
        }
    }

    public static async Task<RootPubKey> FetchRootKeyFromAzure(TokenCredential cred)
    {
        var client = new KeyClient(new Uri(RootKeyVaultUrl), cred);

        // Get the key (public part)
        KeyVaultKey key = await client.GetKeyAsync(RootKeyName);

        // Convert EC JWK to PEM using ECDsa
        if (key.KeyType == KeyType.Ec || key.KeyType == KeyType.EcHsm)
        {
            var jwk = key.Key;
            if (jwk.X == null || jwk.Y == null)
                throw new InvalidOperationException("JWK missing x or y coordinate");

            ECParameters ecParams = new ECParameters
            {
                Q = new ECPoint { X = jwk.X, Y = jwk.Y },
                Curve = jwk.CurveName switch
                {
                    var x when x == KeyCurveName.P256 => ECCurve.NamedCurves.nistP256,
                    var x when x == KeyCurveName.P384 => ECCurve.NamedCurves.nistP384,
                    var x when x == KeyCurveName.P521 => ECCurve.NamedCurves.nistP521,
                    _ => throw new NotSupportedException($"Curve {jwk.CurveName} not supported")
                }
            };

            var ecdsa = ECDsa.Create(ecParams);
            return new RootPubKey(ecdsa);
        }
        else
        {
            throw new InvalidOperationException("Key is not an EC key, cannot convert to PEM.");
        }
    }

    public static RootPubKey ParsePublicRootKey(string pem)
    {
        var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(pem);
        return new RootPubKey(ecdsa);
    }

    /// <summary>
    /// Signs a release file using the provided private key and public key.
    /// </summary>
    public static byte[] SignRelease(string releaseKeyPem, Stream releaseFile)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(releaseKeyPem);

        // Sign the hash of the public key
        return ecdsa.SignData(releaseFile, HashAlgorithmName.SHA256);
    }

    public static bool VerifyRelease(string releaseKeyPem, Stream releaseFile, byte[] sig)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(releaseKeyPem);

        // Verify the signature against the release file
        return ecdsa.VerifyData(releaseFile, sig, HashAlgorithmName.SHA256);
    }

    /// <summary>
    /// Verify that the signature provided for the signing key was created by the root key.
    /// </summary>
    /// <param name="relKey">The contents of the release key file.</param>
    public static bool VerifyReleaseKey(RootPubKey rootKey, byte[] relKey, byte[] sig)
    {
        // Verify the signature against the signing public key
        return rootKey.ECDsa.VerifyData(
            relKey,
            sig,
            HashAlgorithmName.SHA256
        );
    }
}
