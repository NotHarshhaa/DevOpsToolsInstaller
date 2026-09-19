using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace DevOpsToolsInstaller.Services;

public enum SignatureStatus
{
    Valid,
    NotSigned,
    Untrusted,
    InvalidHash,
    Unknown
}

public sealed record AuthenticodeResult(
    SignatureStatus Status,
    string? Publisher,
    string? Issuer,
    DateTime? ValidTo,
    string Summary)
{
    public bool IsValid => Status == SignatureStatus.Valid;

    public string StatusBadge => Status switch
    {
        SignatureStatus.Valid => "Verified Signature",
        SignatureStatus.NotSigned => "Unsigned",
        SignatureStatus.Untrusted => "Untrusted Signature",
        SignatureStatus.InvalidHash => "Tampered / Invalid Hash",
        _ => "Signature Check Failed"
    };

    public string Glyph => Status switch
    {
        SignatureStatus.Valid => "\uE73E",       // Checkmark
        SignatureStatus.NotSigned => "\uE7BA",   // Warning triangle
        SignatureStatus.Untrusted => "\uE783",   // Error / Shield exclamation
        SignatureStatus.InvalidHash => "\uE783", // Error
        _ => "\uE9CE"                           // Info
    };
}

/// <summary>
/// Verifies Authenticode digital signatures on downloaded Windows executables (.exe)
/// and installer packages (.msi) using native Win32 WinVerifyTrust and X509 certificates.
/// </summary>
public static class AuthenticodeService
{
    private static readonly Guid ActionGenericVerifyV2 = new("{00AAC56B-CD44-11d0-8CC2-00C04FC295EE}");

    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOKE_NONE = 0;
    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_STATEACTION_IGNORE = 0;
    private const uint WTD_SAFER_FLAG = 0x00000100;

    private const int ERROR_SUCCESS = 0;
    private const int TRUST_E_NOSIGNATURE = unchecked((int)0x800B0100);
    private const int TRUST_E_BAD_DIGEST = unchecked((int)0x80096010);
    private const int CERT_E_UNTRUSTEDROOT = unchecked((int)0x800B0109);
    private const int CERT_E_EXPIRED = unchecked((int)0x800B0101);
    private const int TRUST_E_EXPLICIT_DISTRUST = unchecked((int)0x800B0111);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(
        IntPtr hwnd,
        [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID,
        IntPtr pWVTData);

    /// <summary>
    /// Checks the digital signature of a local file (.exe, .msi).
    /// </summary>
    public static AuthenticodeResult VerifyFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new AuthenticodeResult(
                SignatureStatus.Unknown, null, null, null, "File not found.");
        }

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext != ".exe" && ext != ".msi" && ext != ".dll")
        {
            return new AuthenticodeResult(
                SignatureStatus.NotSigned, null, null, null,
                $"Digital signature check only applies to executables/installers ({ext} archive).");
        }

        // 1. Try to read X509 certificate to extract publisher metadata
        string? publisher = null;
        string? issuer = null;
        DateTime? validTo = null;

        try
        {
            using var cert = new X509Certificate2(filePath);
            publisher = cert.GetNameInfo(X509NameType.SimpleName, false);
            issuer = cert.GetNameInfo(X509NameType.SimpleName, true);
            validTo = cert.NotAfter;
        }
        catch
        {
            // Certificate reading failed or file is not signed
        }

        // 2. WinVerifyTrust native call
        var fileInfo = new WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
            pcwszFilePath = filePath,
            hFile = IntPtr.Zero,
            pgKnownSubject = IntPtr.Zero
        };

        var pFileInfo = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        var pData = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_DATA>());

        try
        {
            Marshal.StructureToPtr(fileInfo, pFileInfo, false);

            var trustData = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                pPolicyCallbackData = IntPtr.Zero,
                pSIPClientData = IntPtr.Zero,
                dwUIChoice = WTD_UI_NONE,
                fdwRevocationChecks = WTD_REVOKE_NONE,
                dwUnionChoice = WTD_CHOICE_FILE,
                pFile = pFileInfo,
                dwStateAction = WTD_STATEACTION_IGNORE,
                hWVTStateData = IntPtr.Zero,
                pwszURLReference = string.Empty,
                dwProvFlags = WTD_SAFER_FLAG,
                dwUIContext = 0,
                pSignatureSettings = IntPtr.Zero
            };

            Marshal.StructureToPtr(trustData, pData, false);

            var actionGuid = ActionGenericVerifyV2;
            var hResult = WinVerifyTrust(IntPtr.Zero, actionGuid, pData);

            if (hResult == ERROR_SUCCESS)
            {
                var pubName = publisher ?? "Verified Publisher";
                return new AuthenticodeResult(
                    SignatureStatus.Valid,
                    publisher,
                    issuer,
                    validTo,
                    $"Valid digital signature verified ({pubName}).");
            }

            if (hResult == TRUST_E_NOSIGNATURE)
            {
                return new AuthenticodeResult(
                    SignatureStatus.NotSigned,
                    null,
                    null,
                    null,
                    "No digital signature found on this file.");
            }

            if (hResult == TRUST_E_BAD_DIGEST)
            {
                return new AuthenticodeResult(
                    SignatureStatus.InvalidHash,
                    publisher,
                    issuer,
                    validTo,
                    "Digital signature hash mismatch (file may be corrupted or altered).");
            }

            if (hResult == CERT_E_UNTRUSTEDROOT || hResult == TRUST_E_EXPLICIT_DISTRUST)
            {
                return new AuthenticodeResult(
                    SignatureStatus.Untrusted,
                    publisher,
                    issuer,
                    validTo,
                    $"Digital signature is from an untrusted authority ({publisher ?? "Unknown"}).");
            }

            if (hResult == CERT_E_EXPIRED)
            {
                return new AuthenticodeResult(
                    SignatureStatus.Untrusted,
                    publisher,
                    issuer,
                    validTo,
                    $"Digital signature certificate has expired ({publisher ?? "Unknown"}).");
            }

            return new AuthenticodeResult(
                SignatureStatus.Untrusted,
                publisher,
                issuer,
                validTo,
                $"Digital signature verification returned status code 0x{hResult:X8}.");
        }
        catch (Exception ex)
        {
            return new AuthenticodeResult(
                SignatureStatus.Unknown,
                publisher,
                issuer,
                validTo,
                $"Verification error: {ex.Message}");
        }
        finally
        {
            Marshal.FreeHGlobal(pFileInfo);
            Marshal.FreeHGlobal(pData);
        }
    }
}
