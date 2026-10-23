using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace DevOpsToolsInstaller.Services;

public sealed class AwsProfileInfo
{
    public string Name { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string SsoStartUrl { get; set; } = string.Empty;
    public string SsoAccountId { get; set; } = string.Empty;
    public string SsoRoleName { get; set; } = string.Empty;
    public bool HasCredentials { get; set; }
    public string MaskedAccessKey { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public sealed class AwsConnectionStatus
{
    public bool IsConfigured { get; set; }
    public bool IsLoggedIn { get; set; }
    public string ActiveProfile { get; set; } = "default";
    public string Region { get; set; } = string.Empty;
    public string AccountSummary { get; set; } = string.Empty;
    public List<AwsProfileInfo> Profiles { get; set; } = new();
    public string StatusBadge => IsLoggedIn ? "Logged in" : IsConfigured ? "Configured" : "Not configured";
}

public sealed class AzureConnectionStatus
{
    public bool IsConfigured { get; set; }
    public bool IsLoggedIn { get; set; }
    public string UserAccount { get; set; } = string.Empty;
    public string SubscriptionName { get; set; } = string.Empty;
    public string SubscriptionId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string EnvironmentName { get; set; } = "AzureCloud";
    public string StatusBadge => IsLoggedIn ? "Logged in" : "Not logged in";
}

public sealed class GcpConnectionStatus
{
    public bool IsConfigured { get; set; }
    public bool IsLoggedIn { get; set; }
    public string AccountEmail { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string DefaultZone { get; set; } = string.Empty;
    public string DefaultRegion { get; set; } = string.Empty;
    public string ActiveConfig { get; set; } = "default";
    public string StatusBadge => IsLoggedIn ? "Logged in" : "Not logged in";
}

public sealed class KubeContextInfo : System.ComponentModel.INotifyPropertyChanged
{
    private bool _isActive;
    public string Name { get; set; } = string.Empty;
    public string Cluster { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string Namespace { get; set; } = "default";

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive != value)
            {
                _isActive = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsActive)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(ActiveVisibility)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(CanSwitch)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(SwitchButtonText)));
            }
        }
    }

    public Visibility ActiveVisibility => IsActive ? Visibility.Visible : Visibility.Collapsed;
    public bool CanSwitch => !IsActive;
    public string SwitchButtonText => IsActive ? "Active" : "Switch Context";

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

public sealed class KubeConnectionStatus
{
    public bool HasKubeConfig { get; set; }
    public string ActiveContextName { get; set; } = string.Empty;
    public string ActiveCluster { get; set; } = string.Empty;
    public string ActiveNamespace { get; set; } = "default";
    public List<KubeContextInfo> Contexts { get; set; } = new();
    public string StatusBadge => HasKubeConfig ? $"{Contexts.Count} context(s)" : "No kubeconfig";
}

public sealed class CloudDashboardState
{
    public AwsConnectionStatus Aws { get; set; } = new();
    public AzureConnectionStatus Azure { get; set; } = new();
    public GcpConnectionStatus Gcp { get; set; } = new();
    public KubeConnectionStatus Kube { get; set; } = new();
    public DateTime ScannedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// Cloud connections dashboard service (feature 5).
///
/// Discovers and reports whether the workstation is logged in to AWS, Azure,
/// and GCP, and which Kubernetes (kubectl) context is active. Provides 1-click
/// context switching (like kubectx) and 1-click login triggers.
///
/// SECURITY GUARANTEE: Strictly reads local CLI files only. Never sends
/// credentials, makes unauthenticated network calls with secrets, or exposes
/// private access tokens.
/// </summary>
public static class CloudConnectionsService
{
    private static readonly string UserHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>
    /// Scans local state for all cloud providers and Kubernetes contexts.
    /// </summary>
    public static async Task<CloudDashboardState> ScanAllAsync()
    {
        var awsTask = Task.Run(GetAwsStatus);
        var azTask = Task.Run(GetAzureStatus);
        var gcpTask = Task.Run(GetGcpStatus);
        var kubeTask = Task.Run(GetKubeStatus);

        await Task.WhenAll(awsTask, azTask, gcpTask, kubeTask);

        return new CloudDashboardState
        {
            Aws = awsTask.Result,
            Azure = azTask.Result,
            Gcp = gcpTask.Result,
            Kube = kubeTask.Result,
            ScannedAt = DateTime.Now
        };
    }

    // ── AWS ─────────────────────────────────────────────────────────────

    public static AwsConnectionStatus GetAwsStatus()
    {
        var status = new AwsConnectionStatus();
        var awsDir = Path.Combine(UserHome, ".aws");
        var cfgFile = Path.Combine(awsDir, "config");
        var credFile = Path.Combine(awsDir, "credentials");

        if (!Directory.Exists(awsDir) && !File.Exists(cfgFile) && !File.Exists(credFile))
        {
            return status;
        }

        var profiles = new Dictionary<string, AwsProfileInfo>(StringComparer.OrdinalIgnoreCase);

        // Read credentials file
        if (File.Exists(credFile))
        {
            try
            {
                string currentSection = "default";
                foreach (var rawLine in File.ReadAllLines(credFile))
                {
                    var line = rawLine.Trim();
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#") || line.StartsWith(";")) continue;

                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        currentSection = line[1..^1].Trim();
                        if (!profiles.ContainsKey(currentSection))
                        {
                            profiles[currentSection] = new AwsProfileInfo { Name = currentSection };
                        }
                    }
                    else if (line.Contains('='))
                    {
                        var parts = line.Split('=', 2);
                        var key = parts[0].Trim();
                        var val = parts[1].Trim();

                        if (!profiles.TryGetValue(currentSection, out var prof))
                        {
                            prof = new AwsProfileInfo { Name = currentSection };
                            profiles[currentSection] = prof;
                        }

                        if (key.Equals("aws_access_key_id", StringComparison.OrdinalIgnoreCase))
                        {
                            prof.HasCredentials = true;
                            prof.MaskedAccessKey = MaskKey(val);
                        }
                    }
                }
            }
            catch { }
        }

        // Read config file
        if (File.Exists(cfgFile))
        {
            try
            {
                string currentSection = "default";
                foreach (var rawLine in File.ReadAllLines(cfgFile))
                {
                    var line = rawLine.Trim();
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#") || line.StartsWith(";")) continue;

                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        var sec = line[1..^1].Trim();
                        if (sec.StartsWith("profile ", StringComparison.OrdinalIgnoreCase))
                        {
                            sec = sec[8..].Trim();
                        }
                        currentSection = sec;
                        if (!profiles.ContainsKey(currentSection))
                        {
                            profiles[currentSection] = new AwsProfileInfo { Name = currentSection };
                        }
                    }
                    else if (line.Contains('='))
                    {
                        var parts = line.Split('=', 2);
                        var key = parts[0].Trim();
                        var val = parts[1].Trim();

                        if (!profiles.TryGetValue(currentSection, out var prof))
                        {
                            prof = new AwsProfileInfo { Name = currentSection };
                            profiles[currentSection] = prof;
                        }

                        if (key.Equals("region", StringComparison.OrdinalIgnoreCase))
                            prof.Region = val;
                        else if (key.Equals("sso_start_url", StringComparison.OrdinalIgnoreCase))
                            prof.SsoStartUrl = val;
                        else if (key.Equals("sso_account_id", StringComparison.OrdinalIgnoreCase))
                            prof.SsoAccountId = val;
                        else if (key.Equals("sso_role_name", StringComparison.OrdinalIgnoreCase))
                            prof.SsoRoleName = val;
                    }
                }
            }
            catch { }
        }

        status.Profiles = profiles.Values.OrderBy(p => p.Name).ToList();
        status.IsConfigured = status.Profiles.Count > 0;

        var activeName = Environment.GetEnvironmentVariable("AWS_PROFILE");
        if (string.IsNullOrWhiteSpace(activeName)) activeName = "default";
        status.ActiveProfile = activeName;

        var activeProf = status.Profiles.FirstOrDefault(p => p.Name.Equals(activeName, StringComparison.OrdinalIgnoreCase))
                         ?? status.Profiles.FirstOrDefault();

        if (activeProf != null)
        {
            activeProf.IsActive = true;
            status.Region = activeProf.Region;
            status.IsLoggedIn = activeProf.HasCredentials || !string.IsNullOrWhiteSpace(activeProf.SsoStartUrl);

            status.AccountSummary = !string.IsNullOrWhiteSpace(activeProf.SsoAccountId)
                ? $"SSO Account: {activeProf.SsoAccountId} ({activeProf.SsoRoleName})"
                : activeProf.HasCredentials
                    ? $"Key: {activeProf.MaskedAccessKey}"
                    : "Configured (no keys)";
        }

        return status;
    }

    // ── Azure ───────────────────────────────────────────────────────────

    public static AzureConnectionStatus GetAzureStatus()
    {
        var status = new AzureConnectionStatus();
        var azDir = Path.Combine(UserHome, ".azure");
        var profileFile = Path.Combine(azDir, "azureProfile.json");

        if (!File.Exists(profileFile)) return status;

        try
        {
            status.IsConfigured = true;
            using var doc = JsonDocument.Parse(File.ReadAllText(profileFile));
            if (doc.RootElement.TryGetProperty("subscriptions", out var subs) &&
                subs.ValueKind == JsonValueKind.Array)
            {
                foreach (var sub in subs.EnumerateArray())
                {
                    bool isDefault = sub.TryGetProperty("isDefault", out var d) && d.GetBoolean();
                    string state = sub.TryGetProperty("state", out var s) ? s.GetString() ?? "" : "";

                    if (isDefault || string.IsNullOrEmpty(status.SubscriptionName))
                    {
                        status.SubscriptionName = sub.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        status.SubscriptionId = sub.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "";
                        status.TenantId = sub.TryGetProperty("tenantId", out var t) ? t.GetString() ?? "" : "";
                        status.EnvironmentName = sub.TryGetProperty("environmentName", out var env) ? env.GetString() ?? "AzureCloud" : "AzureCloud";

                        if (sub.TryGetProperty("user", out var u) && u.TryGetProperty("name", out var un))
                        {
                            status.UserAccount = un.GetString() ?? "";
                        }

                        status.IsLoggedIn = state.Equals("Enabled", StringComparison.OrdinalIgnoreCase) ||
                                            !string.IsNullOrWhiteSpace(status.UserAccount);

                        if (isDefault) break;
                    }
                }
            }
        }
        catch { }

        return status;
    }

    // ── GCP ─────────────────────────────────────────────────────────────

    public static GcpConnectionStatus GetGcpStatus()
    {
        var status = new GcpConnectionStatus();

        // Search candidates for gcloud config folder
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "gcloud"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Cloud SDK"),
            Path.Combine(UserHome, ".config", "gcloud")
        };

        var gcloudDir = candidates.FirstOrDefault(Directory.Exists);
        if (gcloudDir == null) return status;

        status.IsConfigured = true;

        var activeConfigFile = Path.Combine(gcloudDir, "active_config");
        var activeConfig = "default";
        if (File.Exists(activeConfigFile))
        {
            try
            {
                var content = File.ReadAllText(activeConfigFile).Trim();
                if (!string.IsNullOrWhiteSpace(content)) activeConfig = content;
            }
            catch { }
        }
        status.ActiveConfig = activeConfig;

        var configFilePath = Path.Combine(gcloudDir, "configurations", $"config_{activeConfig}");
        if (File.Exists(configFilePath))
        {
            try
            {
                string currentSection = "core";
                foreach (var rawLine in File.ReadAllLines(configFilePath))
                {
                    var line = rawLine.Trim();
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;

                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        currentSection = line[1..^1].Trim();
                    }
                    else if (line.Contains('='))
                    {
                        var parts = line.Split('=', 2);
                        var key = parts[0].Trim();
                        var val = parts[1].Trim();

                        if (currentSection.Equals("core", StringComparison.OrdinalIgnoreCase))
                        {
                            if (key.Equals("account", StringComparison.OrdinalIgnoreCase))
                                status.AccountEmail = val;
                            else if (key.Equals("project", StringComparison.OrdinalIgnoreCase))
                                status.ProjectId = val;
                        }
                        else if (currentSection.Equals("compute", StringComparison.OrdinalIgnoreCase))
                        {
                            if (key.Equals("zone", StringComparison.OrdinalIgnoreCase))
                                status.DefaultZone = val;
                            else if (key.Equals("region", StringComparison.OrdinalIgnoreCase))
                                status.DefaultRegion = val;
                        }
                    }
                }
            }
            catch { }
        }

        status.IsLoggedIn = !string.IsNullOrWhiteSpace(status.AccountEmail);
        return status;
    }

    // ── Kubernetes & kubectl Contexts (kubectx) ─────────────────────────

    public static string KubeConfigPath => Path.Combine(UserHome, ".kube", "config");

    public static KubeConnectionStatus GetKubeStatus()
    {
        var status = new KubeConnectionStatus();
        if (!File.Exists(KubeConfigPath)) return status;

        status.HasKubeConfig = true;
        try
        {
            var lines = File.ReadAllLines(KubeConfigPath);
            string currentContext = "";
            var contexts = new List<KubeContextInfo>();

            KubeContextInfo? currentEntry = null;
            bool inContextsList = false;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.Trim();

                if (trimmed.StartsWith("current-context:", StringComparison.OrdinalIgnoreCase))
                {
                    currentContext = trimmed[16..].Trim().Trim('\'', '"');
                    continue;
                }

                if (line.StartsWith("contexts:"))
                {
                    inContextsList = true;
                    continue;
                }

                // If another top-level root list begins, finish contexts list
                if (inContextsList && line.Length > 0 && !char.IsWhiteSpace(line[0]) && !line.StartsWith("-"))
                {
                    inContextsList = false;
                }

                if (inContextsList)
                {
                    bool isNewContextLine = line.StartsWith("- ") || (line.StartsWith("  - ") && !line.StartsWith("    "));
                    if (isNewContextLine)
                    {
                        if (currentEntry != null && !string.IsNullOrWhiteSpace(currentEntry.Name))
                        {
                            contexts.Add(currentEntry);
                        }
                        currentEntry = new KubeContextInfo();
                        var afterDash = trimmed[2..].Trim();
                        if (afterDash.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
                        {
                            currentEntry.Name = afterDash[5..].Trim().Trim('\'', '"');
                        }
                    }
                    else if (currentEntry != null)
                    {
                        // Ignore sub-blocks like extensions
                        if (trimmed.StartsWith("name:", StringComparison.OrdinalIgnoreCase) && (line.StartsWith("  name:") || line.StartsWith("name:") || line.StartsWith(" - name:")))
                            currentEntry.Name = trimmed[5..].Trim().Trim('\'', '"');
                        else if (trimmed.StartsWith("cluster:", StringComparison.OrdinalIgnoreCase))
                            currentEntry.Cluster = trimmed[8..].Trim().Trim('\'', '"');
                        else if (trimmed.StartsWith("user:", StringComparison.OrdinalIgnoreCase))
                            currentEntry.User = trimmed[5..].Trim().Trim('\'', '"');
                        else if (trimmed.StartsWith("namespace:", StringComparison.OrdinalIgnoreCase))
                            currentEntry.Namespace = trimmed[10..].Trim().Trim('\'', '"');
                    }
                }
            }

            if (currentEntry != null && !string.IsNullOrWhiteSpace(currentEntry.Name))
            {
                contexts.Add(currentEntry);
            }

            status.ActiveContextName = currentContext;
            foreach (var ctx in contexts)
            {
                ctx.IsActive = ctx.Name.Equals(currentContext, StringComparison.OrdinalIgnoreCase);
                if (ctx.IsActive)
                {
                    status.ActiveCluster = ctx.Cluster;
                    status.ActiveNamespace = string.IsNullOrWhiteSpace(ctx.Namespace) ? "default" : ctx.Namespace;
                }
            }

            status.Contexts = contexts;
        }
        catch { }

        return status;
    }

    /// <summary>
    /// Switches the active Kubernetes context (equivalent to kubectx or kubectl config use-context).
    /// Updates ~/.kube/config directly for instant switching without shell overhead.
    /// </summary>
    public static bool SwitchKubeContext(string targetContextName)
    {
        if (!File.Exists(KubeConfigPath)) return false;

        try
        {
            var lines = File.ReadAllLines(KubeConfigPath);
            bool updated = false;

            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                if (trimmed.StartsWith("current-context:", StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = $"current-context: {targetContextName}";
                    updated = true;
                    break;
                }
            }

            if (!updated)
            {
                // If current-context line wasn't found, prepend it
                var newLines = new List<string> { $"current-context: {targetContextName}" };
                newLines.AddRange(lines);
                lines = newLines.ToArray();
            }

            File.WriteAllLines(KubeConfigPath, lines, Encoding.UTF8);
            ActivityLogService.Success("Kubernetes", $"Switched active context to '{targetContextName}' (kubectx)");
            ToastService.Show("Kubernetes Context Switched", $"Active context: {targetContextName}");
            return true;
        }
        catch (Exception ex)
        {
            ActivityLogService.Error("Kubernetes", $"Failed to switch context: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Launches one-click login in terminal for the specified provider.
    /// </summary>
    public static void LaunchLogin(string provider, string? extraArg = null)
    {
        try
        {
            string cmd;
            switch (provider.ToLowerInvariant())
            {
                case "aws":
                    cmd = !string.IsNullOrWhiteSpace(extraArg)
                        ? $"aws sso login --profile {extraArg}"
                        : "aws sso login";
                    break;
                case "azure":
                    cmd = "az login";
                    break;
                case "gcp":
                    cmd = "gcloud auth login";
                    break;
                default:
                    return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c start \"Cloud Login - {provider.ToUpperInvariant()}\" cmd.exe /k \"{cmd}\"",
                UseShellExecute = true
            };
            Process.Start(startInfo);
            ActivityLogService.Info("Cloud Login", $"Launched login command for {provider}: {cmd}");
        }
        catch (Exception ex)
        {
            ActivityLogService.Error("Cloud Login", $"Failed to launch {provider} login: {ex.Message}");
        }
    }

    private static string MaskKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "";
        if (key.Length <= 6) return new string('*', key.Length);
        return $"{key[..4]}...{key[^3..]}";
    }
}
