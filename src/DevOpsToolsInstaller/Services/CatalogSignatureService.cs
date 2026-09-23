using System.Security.Cryptography;

namespace DevOpsToolsInstaller.Services;

/// <summary>
/// Verifies catalog file signatures against the ECDSA P-256 public key pinned
/// in this binary. The matching private key exists only with the publisher
/// (stored as a GitHub Actions secret / local key file) — a compromised mirror
/// or man-in-the-middle cannot forge catalog or bundle updates, and any
/// tampering with the catalog contents invalidates the signature.
///
/// Signature files (&lt;name&gt;.json.sig) are base64-encoded DER ECDSA
/// signatures over the exact UTF-8 bytes of the JSON file, produced by
/// <c>catalog/sign-catalog.ps1</c>.
/// </summary>
public static class CatalogSignatureService
{
    // SPKI public key — pinned at build time. The private key never ships.
    private const string PinnedPublicKey =
        """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEMKk7Ymmg4o37k3bib5WX3GPuN77X
        hBUXh4u3r+cRgYT8qpZYCowQiS8VvKnZw3sl5Dt5W0/eGDljkDOCD9Rs+A==
        -----END PUBLIC KEY-----
        """;

    /// <summary>
    /// Returns true when <paramref name="data"/> is signed by the pinned key
    /// with <paramref name="base64Signature"/>. Never throws.
    /// </summary>
    public static bool Verify(byte[] data, string base64Signature)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(PinnedPublicKey);
            var signature = Convert.FromBase64String(base64Signature);

            // OpenSSL produces RFC 3279 DER-encoded ECDSA signatures; .NET's
            // default VerifyData expects P1363, so the format must be explicit.
            return ecdsa.VerifyData(
                data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch
        {
            return false;
        }
    }
}
