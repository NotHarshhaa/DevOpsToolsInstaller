using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DevOpsToolsInstaller.Models;

namespace DevOpsToolsInstaller.Services;

public sealed record CliProbeResult(
    bool Success,
    string? DetectedVersion,
    string RawOutput,
    string CommandRun,
    long ElapsedMilliseconds,
    string Message)
{
    public string StatusBadge => Success ? "Healthy" : "Failed / Not Found";
}

/// <summary>
/// Probes DevOps command-line tools to verify execution health, measure latency,
/// and detect active versions on the workstation.
/// </summary>
public static class CliHealthService
{
    private static readonly Dictionary<string, (string ExeName, string VersionArgs)> ToolCliMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Kubernetes
            { "kubectl", ("kubectl.exe", "version --client") },
            { "helm", ("helm.exe", "version --short") },
            { "k9s", ("k9s.exe", "version --short") },
            { "kind", ("kind.exe", "version") },
            { "minikube", ("minikube.exe", "version") },

            // IaC
            { "terraform", ("terraform.exe", "version") },
            { "opentofu", ("tofu.exe", "version") },
            { "pulumi", ("pulumi.exe", "version") },

            // Cloud CLIs
            { "awscli", ("aws.exe", "--version") },
            { "azure-cli", ("az.cmd", "version") },
            { "gcloud-cli", ("gcloud.cmd", "version") },
            { "oci-cli", ("oci.exe", "--version") },

            // Containers
            { "docker-desktop", ("docker.exe", "--version") },
            { "podman-desktop", ("podman.exe", "--version") },

            // CI/CD & Git
            { "git", ("git.exe", "--version") },
            { "github-cli", ("gh.exe", "--version") },
            { "gitlab-cli", ("glab.exe", "--version") },
            { "argocd", ("argocd.exe", "version --client") },
            { "argocd-cli", ("argocd.exe", "version --client") },

            // Security
            { "trivy", ("trivy.exe", "--version") },
            { "grype", ("grype.exe", "version") },
            { "syft", ("syft.exe", "version") },
            { "cosign", ("cosign.exe", "version") },
            { "snyk", ("snyk.exe", "--version") },
            { "snyk-cli", ("snyk.exe", "--version") },

            // Utilities
            { "jq", ("jq.exe", "--version") },
            { "yq", ("yq.exe", "--version") },
            { "curl", ("curl.exe", "--version") },
            { "vscode", ("code.cmd", "--version") }
        };

    /// <summary>
    /// Gets the primary executable name and version argument for a catalog tool.
    /// </summary>
    public static (string ExeName, string VersionArgs) GetProbeCommand(ToolDefinition tool)
    {
        if (ToolCliMap.TryGetValue(tool.Id, out var mapped))
        {
            return mapped;
        }

        // Default inference
        var ext = Path.GetExtension(tool.FileName).ToLowerInvariant();
        if (ext == ".exe")
        {
            return (tool.FileName, "--version");
        }

        return ($"{tool.Id}.exe", "--version");
    }

    /// <summary>
    /// Attempts to locate the executable on the filesystem (Tools\bin, Tools\<id>, or system PATH).
    /// </summary>
    public static string? ResolveExecutablePath(string exeName, string? toolId = null)
    {
        // 1. Check Tools\bin
        var inBin = Path.Combine(ArtifactService.BinFolder, exeName);
        if (File.Exists(inBin)) return inBin;

        // 2. Check Tools\<toolId> if provided
        if (!string.IsNullOrEmpty(toolId))
        {
            var inToolFolder = Path.Combine(ArtifactService.ToolsRoot, toolId);
            if (Directory.Exists(inToolFolder))
            {
                var match = Directory.GetFiles(inToolFolder, exeName, SearchOption.AllDirectories).FirstOrDefault();
                if (match != null) return match;
            }
        }

        // 3. Check system PATH
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        var userPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
        var combined = $"{ArtifactService.BinFolder};{userPath};{pathVar}";

        var dirs = combined.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var dir in dirs)
        {
            try
            {
                var candidate = Path.Combine(dir.Trim().Trim('\"'), exeName);
                if (File.Exists(candidate)) return candidate;

                if (!exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                    !exeName.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) &&
                    !exeName.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(candidate + ".exe")) return candidate + ".exe";
                    if (File.Exists(candidate + ".cmd")) return candidate + ".cmd";
                }
            }
            catch
            {
                // Invalid path format
            }
        }

        return null;
    }

    /// <summary>
    /// Executes a tool's version command asynchronously with a strict timeout.
    /// </summary>
    public static async Task<CliProbeResult> ProbeToolAsync(
        ToolDefinition tool,
        int timeoutSeconds = 3,
        CancellationToken ct = default)
    {
        var (exeName, args) = GetProbeCommand(tool);
        var resolvedPath = ResolveExecutablePath(exeName, tool.Id);

        var targetCommand = resolvedPath ?? exeName;
        var sw = Stopwatch.StartNew();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = targetCommand,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // Set environment PATH to include Tools\bin
            var currentPath = psi.EnvironmentVariables["PATH"] ?? "";
            psi.EnvironmentVariables["PATH"] = $"{ArtifactService.BinFolder};{currentPath}";

            using var proc = new Process { StartInfo = psi };
            proc.Start();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            var stdoutTask = proc.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = proc.StandardError.ReadToEndAsync(cts.Token);

            var exited = await Task.Run(() => proc.WaitForExit(timeoutSeconds * 1000), cts.Token);
            sw.Stop();

            if (!exited)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return new CliProbeResult(
                    false,
                    null,
                    "Process timed out after 3 seconds.",
                    $"{exeName} {args}",
                    sw.ElapsedMilliseconds,
                    "Health check timed out.");
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var combinedOutput = string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;

            var version = ExtractVersion(combinedOutput);
            bool isHealthy = proc.ExitCode == 0 || !string.IsNullOrEmpty(version);

            return new CliProbeResult(
                isHealthy,
                version,
                combinedOutput.Trim(),
                $"{exeName} {args}",
                sw.ElapsedMilliseconds,
                isHealthy ? $"CLI responded in {sw.ElapsedMilliseconds}ms." : $"Exited with code {proc.ExitCode}.");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new CliProbeResult(
                false,
                null,
                ex.Message,
                $"{exeName} {args}",
                sw.ElapsedMilliseconds,
                $"Could not execute {exeName}: {ex.Message}");
        }
    }

    /// <summary>
    /// Parses a semantic version string from CLI version output.
    /// </summary>
    public static string? ExtractVersion(string rawOutput)
    {
        if (string.IsNullOrWhiteSpace(rawOutput)) return null;

        var firstLine = rawOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

        // Look for semver pattern v?X.Y.Z
        var match = Regex.Match(firstLine, @"\bv?(\d+\.\d+(\.\d+)?(-[a-zA-Z0-9\.\-_]+)?)\b");
        if (match.Success)
        {
            return match.Groups[1].Value.TrimStart('v', 'V');
        }

        return firstLine.Length > 30 ? firstLine[..30] + "…" : firstLine;
    }
}
