using System.Collections.Concurrent;
using System.Security.Cryptography;
using DevOpsToolsInstaller.Models;

namespace DevOpsToolsInstaller.Services;

public sealed class DownloadService
{
    private static readonly HttpClient Http;
    private static readonly ConcurrentDictionary<string, Task> ActiveDownloads = new();

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
            "DevOpsToolsInstaller/1.2.0 (Windows NT 10.0; Win64; x64)");
    }

    /// <summary>
    /// Default download folder: %LOCALAPPDATA%\DevOpsToolsInstaller\Downloads
    /// </summary>
    public static string DefaultDownloadsFolder
    {
        get
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DevOpsToolsInstaller", "Downloads");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

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
        var downloadTask = ActiveDownloads.GetOrAdd(
            tool.Id,
            _ => ExecuteDownloadAsync(tool, destinationFolder, progress, ct));

        try
        {
            await downloadTask;
        }
        finally
        {
            ActiveDownloads.TryRemove(tool.Id, out _);
        }
    }

    private static async Task ExecuteDownloadAsync(
        ToolDefinition tool,
        string destinationFolder,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        var destPath = Path.Combine(destinationFolder, tool.FileName);

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
                    return;
                }
            }

            // 0-byte file or hash mismatch — re-download
            CleanupPartial(destPath);
        }

        tool.Status = ToolStatus.Downloading;

        try
        {
            using var response = await Http.GetAsync(
                tool.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                ct);

            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            await using (var stream = await response.Content.ReadAsStreamAsync(ct))
            await using (var fileStream = File.Create(destPath))
            {
                var buffer = new byte[81920]; // 80 KB chunks
                long totalRead = 0;
                int bytesRead;
                int lastReportedPct = -1;

                while ((bytesRead = await stream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    totalRead += bytesRead;

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
            if (!string.IsNullOrEmpty(tool.Sha256) && !await VerifyHashAsync(destPath, tool.Sha256, ct))
            {
                CleanupPartial(destPath);
                throw new CryptographicException("SHA256 checksum verification failed.");
            }

            tool.Progress = 100;
            tool.Status = ToolStatus.Downloaded;
        }
        catch (OperationCanceledException)
        {
            CleanupPartial(destPath);
            tool.Status = ToolStatus.NotDownloaded;
            tool.Progress = 0;
            throw;
        }
        catch (Exception ex)
        {
            CleanupPartial(destPath);
            tool.Status = ToolStatus.Failed;
            tool.StatusText = $"Failed: {ex.Message}";
            tool.Progress = 0;
            throw;
        }
    }

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
