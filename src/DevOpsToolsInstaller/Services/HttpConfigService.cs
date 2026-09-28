using System.Net;

namespace DevOpsToolsInstaller.Services;

/// <summary>
/// Central configuration for every outbound HttpClient (downloads, catalog,
/// update checks). Applies the user's proxy settings from Settings → Network
/// so corporate users behind a proxy can reach vendor endpoints. When no
/// custom proxy is configured, handlers keep the .NET default behaviour
/// (use the system proxy).
/// </summary>
public static class HttpConfigService
{
    /// <summary>
    /// Applies the configured proxy to a freshly created handler.
    /// </summary>
    public static void Apply(HttpClientHandler handler)
    {
        if (!SettingsService.ProxyEnabled) return;

        var raw = SettingsService.ProxyUrl.Trim();
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var proxyUri) ||
            (proxyUri.Scheme != Uri.UriSchemeHttp && proxyUri.Scheme != Uri.UriSchemeHttps))
        {
            ActivityLogService.Warn("Network", $"Invalid proxy address '{raw}' — falling back to the system proxy.");
            return;
        }

        var proxy = new WebProxy(proxyUri);
        if (!string.IsNullOrWhiteSpace(SettingsService.ProxyUsername))
        {
            proxy.Credentials = new NetworkCredential(
                SettingsService.ProxyUsername, SettingsService.ProxyPassword);
        }

        handler.Proxy = proxy;
        handler.UseProxy = true;
    }

    /// <summary>
    /// Creates an HttpClientHandler pre-configured with the user's proxy settings.
    /// </summary>
    public static HttpClientHandler CreateHandler() => CreateHandler(allowAutoRedirect: true);

    public static HttpClientHandler CreateHandler(bool allowAutoRedirect)
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = allowAutoRedirect };
        Apply(handler);
        return handler;
    }
}
