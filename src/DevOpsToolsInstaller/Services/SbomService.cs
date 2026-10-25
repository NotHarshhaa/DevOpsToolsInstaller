using System.Text;
using System.Text.Json;
using DevOpsToolsInstaller.Models;

namespace DevOpsToolsInstaller.Services;

public enum SbomFormat
{
    /// <summary>CycloneDX 1.5 JSON — the de-facto standard for tooling pipelines.</summary>
    CycloneDx,
    /// <summary>SPDX 2.3 JSON — required by some government/enterprise compliance programs.</summary>
    Spdx
}

/// <summary>
/// Exports the workstation's installed toolset as a Software Bill of Materials
/// (SBOM) for compliance, procurement, and vulnerability-management pipelines.
/// Two industry-standard formats are supported: CycloneDX 1.5 and SPDX 2.3.
///
/// Components are emitted for every installed catalog tool (including private
/// feeds and user-authored custom tools) with the detected or catalog version,
/// the vendor download URL, the catalog's SHA-256 artifact hash, the homepage,
/// and the SPDX license identifier when the catalog (or a custom tool) declares
/// one. Nothing here launches processes or touches the network — versions come
/// from the last scan, exactly like the markdown workstation report.
/// </summary>
public static class SbomService
{
    public static string DefaultSbomPath(SbomFormat format)
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            $"DevOpsToolsInstaller-sbom-{DateTime.Now:yyyyMMdd-HHmm}{FileExtension(format)}");

    public static string FileExtension(SbomFormat format)
        => format == SbomFormat.Spdx ? ".spdx.json" : ".cdx.json";

    /// <summary>
    /// Builds the SBOM and writes it to <paramref name="path"/>.
    /// Returns the number of installed tools included as components.
    /// </summary>
    public static async Task<int> WriteSbomAsync(
        IEnumerable<ToolDefinition> tools, string path,
        SbomFormat format = SbomFormat.CycloneDx, CancellationToken ct = default)
    {
        var (json, installedCount) = await BuildAsync(tools, format, ct);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(false), ct);
        return installedCount;
    }

    public static async Task<(string Json, int InstalledCount)> BuildAsync(
        IEnumerable<ToolDefinition> tools, SbomFormat format = SbomFormat.CycloneDx,
        CancellationToken ct = default)
    {
        var installed = await ResolveInstalledAsync(tools, ct);

        var json = format == SbomFormat.Spdx
            ? BuildSpdx(installed)
            : BuildCycloneDx(installed);

        return (json, installed.Count);
    }

    /// <summary>
    /// Resolves which catalog tools count as installed and fills in detected
    /// versions — the same rules the markdown workstation report uses, without
    /// launching any processes.
    /// </summary>
    private static Task<List<ToolDefinition>> ResolveInstalledAsync(
        IEnumerable<ToolDefinition> tools, CancellationToken ct)
    {
        var dlFolder = DownloadService.DefaultDownloadsFolder;
        return Task.Run(() =>
        {
            var installed = new List<ToolDefinition>();
            foreach (var tool in tools)
            {
                if (UninstallService.IsInstalled(tool, dlFolder) || tool.Status == ToolStatus.Downloaded)
                {
                    tool.IsInstalled = true;
                    if (tool.Kind == ArtifactKind.Installer && string.IsNullOrWhiteSpace(tool.DetectedVersion))
                    {
                        var v = UninstallService.GetInstalledVersion(tool);
                        if (!string.IsNullOrWhiteSpace(v)) tool.DetectedVersion = v;
                    }
                    installed.Add(tool);
                }
            }
            return installed;
        }, ct);
    }

    private static string EffectiveVersion(ToolDefinition tool)
        => string.IsNullOrWhiteSpace(tool.DetectedVersion) ? tool.Version : tool.DetectedVersion;

    /// <summary>SPDX ids are letters, digits, and <c>. - +</c>; expressions
    /// (e.g. "MIT OR Apache-2.0") are declared as license names instead.</summary>
    private static bool LooksLikeSpdxId(string value)
        => value.Length > 0 && value.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '+');

    // ── CycloneDX 1.5 ──────────────────────────────────────────────────

    private static string BuildCycloneDx(List<ToolDefinition> installed)
    {
        var components = new List<object>(installed.Count);

        foreach (var tool in installed.OrderBy(t => t.Category).ThenBy(t => t.Name))
        {
            var version = EffectiveVersion(tool);
            var purl = $"pkg:generic/{Uri.EscapeDataString(tool.Id)}@{Uri.EscapeDataString(version)}";

            var component = new Dictionary<string, object?>
            {
                ["type"] = "application",
                ["bom-ref"] = purl,
                ["name"] = tool.Id,
                ["version"] = version,
            };

            if (!string.IsNullOrWhiteSpace(tool.Description))
                component["description"] = tool.Description;

            if (!string.IsNullOrWhiteSpace(tool.Homepage))
                component["homepage"] = tool.Homepage;

            if (!string.IsNullOrWhiteSpace(tool.DownloadUrl))
                component["externalReferences"] = new[]
                {
                    new Dictionary<string, string>
                    {
                        ["type"] = "distribution",
                        ["url"] = tool.DownloadUrl
                    }
                };

            if (tool.Sha256.Length == 64 && tool.Sha256.All(Uri.IsHexDigit))
                component["hashes"] = new[] { new Dictionary<string, string> { ["alg"] = "SHA-256", ["content"] = tool.Sha256.ToLowerInvariant() } };

            if (!string.IsNullOrWhiteSpace(tool.License))
            {
                var licenseObject = LooksLikeSpdxId(tool.License)
                    ? new Dictionary<string, string> { ["id"] = tool.License }
                    : new Dictionary<string, string> { ["name"] = tool.License };
                component["licenses"] = new[] { new Dictionary<string, object> { ["license"] = licenseObject } };
            }

            component["purl"] = purl;

            var properties = new List<Dictionary<string, string>>
            {
                new() { ["name"] = "devopstoolsinstaller:displayName", ["value"] = tool.Name },
                new() { ["name"] = "devopstoolsinstaller:category", ["value"] = tool.Category },
                new() { ["name"] = "devopstoolsinstaller:versionSource", ["value"] = string.IsNullOrWhiteSpace(tool.DetectedVersion) ? "catalog" : "detected" }
            };
            if (!string.IsNullOrEmpty(tool.FeedName))
                properties.Add(new Dictionary<string, string> { ["name"] = "devopstoolsinstaller:corporateFeed", ["value"] = tool.FeedName });
            component["properties"] = properties;

            components.Add(component);
        }

        var bom = new Dictionary<string, object?>
        {
            ["bomFormat"] = "CycloneDX",
            ["specVersion"] = "1.5",
            ["serialNumber"] = $"urn:uuid:{Guid.NewGuid()}",
            ["version"] = 1,
            ["metadata"] = new Dictionary<string, object?>
            {
                ["timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                ["tools"] = new
                {
                    components = new[]
                    {
                        new Dictionary<string, string>
                        {
                            ["type"] = "application",
                            ["name"] = "DevOpsToolsInstaller",
                            ["version"] = AppUpdaterService.CurrentVersion
                        }
                    }
                },
                ["component"] = new Dictionary<string, string>
                {
                    ["type"] = "device",
                    ["name"] = Environment.MachineName,
                    ["description"] = $"Windows workstation provisioned by DevOpsToolsInstaller (user: {Environment.UserName})"
                }
            },
            ["components"] = components
        };

        return JsonSerializer.Serialize(bom, new JsonSerializerOptions { WriteIndented = true });
    }

    // ── SPDX 2.3 ───────────────────────────────────────────────────────

    private static string BuildSpdx(List<ToolDefinition> installed)
    {
        var packages = new List<object>(installed.Count);
        var relationships = new List<object>(installed.Count);
        var describes = new List<string>(installed.Count);

        foreach (var tool in installed.OrderBy(t => t.Category).ThenBy(t => t.Name))
        {
            var version = EffectiveVersion(tool);
            var spdxId = $"SPDXRef-Package-{SanitizeSpdxRef(tool.Id)}";
            var purl = $"pkg:generic/{Uri.EscapeDataString(tool.Id)}@{Uri.EscapeDataString(version)}";
            var license = string.IsNullOrWhiteSpace(tool.License) ? "NOASSERTION" : tool.License;
            var downloadLocation = !string.IsNullOrWhiteSpace(tool.DownloadUrl)
                ? tool.DownloadUrl
                : !string.IsNullOrWhiteSpace(tool.Homepage) ? tool.Homepage : "NOASSERTION";

            var package = new Dictionary<string, object?>
            {
                ["name"] = tool.Id,
                ["SPDXID"] = spdxId,
                ["versionInfo"] = version,
                ["downloadLocation"] = downloadLocation,
                ["filesAnalyzed"] = false,
                ["licenseConcluded"] = license,
                ["licenseDeclared"] = license,
                ["copyrightText"] = "NOASSERTION",
                ["supplier"] = "NOASSERTION",
                ["primaryPackagePurpose"] = "APPLICATION",
                ["externalRefs"] = new[]
                {
                    new Dictionary<string, string>
                    {
                        ["referenceCategory"] = "PACKAGE-MANAGER",
                        ["referenceType"] = "purl",
                        ["referenceLocator"] = purl
                    }
                }
            };

            if (!string.IsNullOrWhiteSpace(tool.Name))
                package["summary"] = tool.Name;
            if (!string.IsNullOrWhiteSpace(tool.Description))
                package["description"] = tool.Description;
            if (!string.IsNullOrWhiteSpace(tool.Homepage))
                package["homepage"] = tool.Homepage;
            if (!string.IsNullOrEmpty(tool.FeedName))
                package["comment"] = $"Sourced from corporate catalog feed '{tool.FeedName}'.";

            packages.Add(package);
            relationships.Add(new
            {
                spdxElementId = "SPDXRef-DOCUMENT",
                relatedSpdxElement = spdxId,
                relationshipType = "DESCRIBES"
            });
            describes.Add(spdxId);
        }

        var doc = new Dictionary<string, object?>
        {
            ["spdxVersion"] = "SPDX-2.3",
            ["dataLicense"] = "CC0-1.0",
            ["SPDXID"] = "SPDXRef-DOCUMENT",
            ["name"] = $"DevOpsToolsInstaller-{Environment.MachineName}-{DateTime.UtcNow:yyyyMMdd-HHmmss}",
            ["documentNamespace"] = $"https://github.com/NotHarshhaa/DevOpsToolsInstaller/sbom/{Guid.NewGuid()}",
            ["creationInfo"] = new Dictionary<string, object?>
            {
                ["created"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                ["creators"] = new[]
                {
                    $"Tool: DevOpsToolsInstaller-{AppUpdaterService.CurrentVersion}",
                    $"Person: {Environment.UserName}",
                    $"Organization: {Environment.MachineName}"
                }
            },
            ["packages"] = packages,
            ["relationships"] = relationships,
            ["describes"] = describes
        };

        return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string SanitizeSpdxRef(string id)
    {
        var chars = id.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '-').ToArray();
        var safe = new string(chars).Trim('.');
        return safe.Length == 0 ? "tool" : safe;
    }
}
