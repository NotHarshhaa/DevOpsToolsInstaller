using System.Collections.Concurrent;
using System.Security.Cryptography;
using DevOpsToolsInstaller.Models;

namespace DevOpsToolsInstaller.Services;

public sealed class DownloadService
{
    private static readonly HttpClient Http;
    private static readonly ConcurrentDictionary<string, Lazy<Task>> ActiveDownloads = new();

    static DownloadService()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true
        };
        Http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(30)
        };
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"DevOpsToolsInstaller/{AppUpdaterService.CurrentVersion} (Windows NT 10.0; Win64; x64)");
    }

    /// <summary>
    /// Current download folder configured by the user, defaulting to %LOCALAPPDATA%\DevOpsToolsInstaller\Downloads.
    /// </summary>
    public static string DefaultDownloadsFolder => SettingsService.DownloadsFolder;

    /// <summary>
    /// Downloads a single tool's installer with progress reporting.
    /// Skips if the file already exists, has non-zero size, and SHA256 matches.
    /// Deduplicates concurrent download requests for the same tool.
    /// </summary>
    public async Task DownloadAsync(
        ToolDefinition tool,
        string destinationFolder,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        // Lazy<Task> guarantees exactly one download starts per tool even when
        // concurrent callers race GetOrAdd (a plain factory can run twice).
        var downloadTask = ActiveDownloads.GetOrAdd(
            tool.Id,
            _ => new Lazy<Task>(() => ExecuteDownloadAsync(tool, destinationFolder, progress, ct),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            await downloadTask.Value;
        }
        finally
        {
            ActiveDownloads.TryRemove(tool.Id, out _);
        }
    }

    private const int MaxDownloadAttempts = 3;

    private static async Task ExecuteDownloadAsync(
        ToolDefinition tool,
        string destinationFolder,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        // Security: refuse to fetch executables over plain-text HTTP — an
        // attacker on the network could otherwise swap the binary mid-flight
        // even when the catalog itself is fetched over HTTPS.
        if (!Uri.TryCreate(tool.DownloadUrl, UriKind.Absolute, out var downloadUri) ||
            downloadUri.Scheme != Uri.UriSchemeHttps)
        {
            var msg = $"Blocked download: '{tool.DownloadUrl}' is not HTTPS.";
            ActivityLogService.Error(tool.Name, msg);
            throw new InvalidOperationException(msg);
        }

        var destPath = Path.Combine(destinationFolder, tool.FileName);
        var partialPath = destPath + ".partial";
        var metaPath = partialPath + ".meta";

        // Skip if already downloaded, non-empty, and hash matches
        if (File.Exists(destPath))
        {
            var fileInfo = new FileInfo(destPath);
            if (fileInfo.Length > 0)
            {
                if (string.IsNullOrEmpty(tool.Sha256) || await VerifyHashAsync(destPath, tool.Sha256, ct))
                {
                    tool.Progress = 100;
                    tool.Status = ToolStatus.Downloaded;
                    progress?.Report(100);
                    // The transfer is complete — stale resume state is no longer needed.
                    CleanupPartial(partialPath);
                    CleanupPartial(metaPath);
                    return;
                }
            }

            // 0-byte file or hash mismatch — discard it and re-download
            CleanupPartial(destPath);
        }

        tool.Status = ToolStatus.Downloading;
        ActivityLogService.Info(tool.Name, "Download started");

        try
        {
            // Transient network failures (dropped Wi-Fi, DNS blips, reset
            // connections) are retried with exponential backoff — each retry
            // resumes from the .partial file instead of starting over.
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    await DownloadCoreAsync(tool, destPath, partialPath, metaPath, progress, ct);
                    break;
                }
                catch (Exception ex) when (attempt < MaxDownloadAttempts && IsTransient(ex))
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                    ActivityLogService.Warn(tool.Name,
                        $"Transient failure ({ex.Message}) — retrying in {delay.TotalSeconds:F0}s " +
                        $"(attempt {attempt + 1} of {MaxDownloadAttempts})");
                    await Task.Delay(delay, ct);
                }
            }

            tool.DownloadSpeed = string.Empty;
            tool.Progress = 100;
            tool.Status = ToolStatus.Downloaded;
            ActivityLogService.Success(tool.Name, "Download completed successfully");
        }
        catch (OperationCanceledException)
        {
            // Keep the .partial file so the next attempt resumes from here.
            tool.DownloadSpeed = string.Empty;
            tool.Status = ToolStatus.NotDownloaded;
            tool.Progress = 0;
            ActivityLogService.Warn(tool.Name, "Download cancelled — progress saved for resume");
            throw;
        }
        catch (Exception ex)
        {
            // Keep the .partial file so the next attempt resumes from here,
            // unless the failure means the partial is unusable.
            if (ex is CryptographicException)
            {
                CleanupPartial(partialPath);
            }
            tool.DownloadSpeed = string.Empty;
            tool.Status = ToolStatus.Failed;
            tool.StatusText = $"Failed: {ex.Message}";
            tool.Progress = 0;
            ActivityLogService.Error(tool.Name, $"Download failed: {ex.Message}");
            throw;
        }
    }

    private static async Task DownloadCoreAsync(
        ToolDefinition tool,
        string destPath,
        string partialPath,
        string metaPath,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        // Resume support: a leftover .partial from an interrupted attempt
        // is continued via an HTTP Range request when the server allows it.
        long resumeOffset = 0;
        string? storedEtag = null;
        if (File.Exists(partialPath))
        {
            resumeOffset = new FileInfo(partialPath).Length;
            if (File.Exists(metaPath))
            {
                storedEtag = (await File.ReadAllTextAsync(metaPath, ct)).Trim();
            }
        }

        bool resumed = false;
        HttpResponseMessage? response = null;
        while (true)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, tool.DownloadUrl);
            if (resumeOffset > 0)
            {
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(resumeOffset, null);
            }

            var candidate = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            candidate.EnsureSuccessStatusCode();

            if (resumeOffset > 0 && candidate.StatusCode == System.Net.HttpStatusCode.PartialContent &&
                CanResumeSafely(candidate.Headers.ETag?.Tag, storedEtag))
            {
                resumed = true;
                response = candidate;
                ActivityLogService.Info(tool.Name,
                    $"Resuming download from {resumeOffset / (1024.0 * 1024.0):F1} MB");
                break;
            }

            if (resumeOffset == 0)
            {
                response = candidate;
                break;
            }

            // The server ignored the Range request, or the remote artifact has
            // changed since the .partial was written (e.g. a version-pinned URL
            // now serves a newer build). Resuming would splice bytes from two
            // different artifacts — discard the partial and restart.
            ActivityLogService.Info(tool.Name,
                "Partial file no longer matches the remote artifact — restarting download");
            candidate.Dispose();
            CleanupPartial(partialPath);
            CleanupPartial(metaPath);
            resumeOffset = 0;
            storedEtag = null;
        }

        using (response)
        {
            // Persist the artifact's identity (ETag) so a future resume can
            // verify the .partial still belongs to the same file.
            if (!resumed)
            {
                var freshEtag = response!.Headers.ETag?.Tag;
                if (freshEtag is not null)
                {
                    await File.WriteAllTextAsync(metaPath, freshEtag, ct);
                }
                else
                {
                    CleanupPartial(metaPath);
                }
            }

            var totalBytes = response.Content.Headers.ContentLength is { } len
                ? len + resumeOffset
                : -1L;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long lastSpeedBytes = 0;
            long lastSpeedTimeMs = 0;

            await using (var stream = await response.Content.ReadAsStreamAsync(ct))
            await using (var fileStream = new FileStream(
                partialPath,
                resumed ? FileMode.Append : FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                true))
            {
                var buffer = new byte[81920]; // 80 KB chunks
                long totalRead = resumed ? resumeOffset : 0;
                int bytesRead;
                int lastReportedPct = -1;

                while ((bytesRead = await stream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    totalRead += bytesRead;

                    var elapsedMs = sw.ElapsedMilliseconds;
                    if (elapsedMs - lastSpeedTimeMs >= 400)
                    {
                        var deltaBytes = totalRead - lastSpeedBytes;
                        var deltaTimeSec = (elapsedMs - lastSpeedTimeMs) / 1000.0;
                        var speedMBps = deltaTimeSec > 0 ? (deltaBytes / (1024.0 * 1024.0)) / deltaTimeSec : 0;
                        tool.DownloadSpeed = $"{speedMBps:F1} MB/s";
                        lastSpeedBytes = totalRead;
                        lastSpeedTimeMs = elapsedMs;
                    }

                    if (totalBytes > 0)
                    {
                        var pct = (double)totalRead / totalBytes * 100;

                        // Throttle UI notification to whole-number changes
                        var wholePct = (int)pct;
                        if (wholePct != lastReportedPct)
                        {
                            lastReportedPct = wholePct;
                            tool.Progress = pct;
                            progress?.Report(pct);
                        }
                    }
                }

                // Verify download was not cut short
                if (totalBytes > 0 && totalRead < totalBytes)
                {
                    throw new IOException(
                        $"Download ended prematurely ({totalRead} of {totalBytes} bytes received).");
                }
            }

            // Verify hash if specified
            if (!string.IsNullOrEmpty(tool.Sha256) && !await VerifyHashAsync(partialPath, tool.Sha256, ct))
            {
                // A corrupt partial cannot be resumed — discard it entirely.
                CleanupPartial(partialPath);
                throw new CryptographicException("SHA256 checksum verification failed.");
            }

            // Promote the verified partial file to its final name.
            if (File.Exists(destPath))
            {
                File.Delete(destPath);
            }
            File.Move(partialPath, destPath);
            CleanupPartial(metaPath);

            // Security: tag the file with Mark-of-the-Web (Zone.Identifier) so
            // Windows SmartScreen / Microsoft Defender evaluate it the same way
            // as a browser download.
            ApplyMarkOfTheWeb(destPath);
        }
    }

    /// <summary>
    /// Decides whether an interrupted .partial may safely be resumed against
    /// the current response. When the server publishes an ETag, it must match
    /// the one recorded alongside the partial; when the server publishes none,
    /// there is nothing to compare and the resume proceeds as before.
    /// </summary>
    private static bool CanResumeSafely(string? currentEtag, string? storedEtag)
    {
        if (currentEtag is null) return true;
        if (storedEtag is null) return false;
        return string.Equals(storedEtag, currentEtag, StringComparison.Ordinal);
    }

    /// <summary>
    /// Network-level faults worth retrying with backoff. User cancellation
    /// (OperationCanceledException) and verification failures are excluded.
    /// </summary>
    private static bool IsTransient(Exception ex)
        => ex is HttpRequestException or IOException;

    /// <summary>
    /// Downloads multiple tools concurrently with a semaphore throttle.
    /// </summary>
    public async Task DownloadBatchAsync(
        IEnumerable<ToolDefinition> tools,
        string destinationFolder,
        int maxConcurrency = 3,
        CancellationToken ct = default)
    {
        using var semaphore = new SemaphoreSlim(maxConcurrency);

        var tasks = tools.Select(async tool =>
        {
            try
            {
                await semaphore.WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                tool.Status = ToolStatus.NotDownloaded;
                tool.Progress = 0;
                return;
            }

            try
            {
                await DownloadAsync(tool, destinationFolder, progress: null, ct);
            }
            catch (OperationCanceledException)
            {
                tool.Status = ToolStatus.NotDownloaded;
                tool.Progress = 0;
            }
            catch (Exception ex)
            {
                tool.Status = ToolStatus.Failed;
                tool.StatusText = $"Failed: {ex.Message}";
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Verifies a file's SHA256 against the expected hash.
    /// </summary>
    private static async Task<bool> VerifyHashAsync(
        string filePath, string expectedHash, CancellationToken ct)
    {
        try
        {
            await using var fs = File.OpenRead(filePath);
            var hashBytes = await SHA256.HashDataAsync(fs, ct);
            var actualHash = Convert.ToHexString(hashBytes);
            return string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Removes a partially-downloaded file safely.
    /// </summary>
    private static void CleanupPartial(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            /* best effort */
        }
    }

    /// <summary>
    /// Writes the Windows Mark-of-the-Web (Zone.Identifier alternate data
    /// stream, ZoneId=3 = Internet) onto a downloaded file so that
    /// SmartScreen and Microsoft Defender treat it as an internet download.
    /// Best-effort: a FAT/odd filesystem without ADS support is ignored.
    /// </summary>
    private static void ApplyMarkOfTheWeb(string filePath)
    {
        try
        {
            File.WriteAllText(
                filePath + ":Zone.Identifier",
                "[ZoneTransfer]\r\nZoneId=3\r\n");
        }
        catch
        {
            // Volume may not support alternate data streams.
        }
    }

    /// <summary>
    /// Checks whether a tool's installer has already been downloaded and is non-empty.
    /// </summary>
    public static bool IsAlreadyDownloaded(ToolDefinition tool, string destinationFolder)
    {
        var path = Path.Combine(destinationFolder, tool.FileName);
        return File.Exists(path) && new FileInfo(path).Length > 0;
    }

    /// <summary>
    /// Deletes all files in the downloads folder.
    /// </summary>
    public static long ClearDownloads(string folder)
    {
        if (!Directory.Exists(folder)) return 0;

        long freed = 0;
        foreach (var file in Directory.GetFiles(folder))
        {
            try
            {
                freed += new FileInfo(file).Length;
                File.Delete(file);
            }
            catch
            {
                /* skip locked files */
            }
        }
        return freed;
    }
}
