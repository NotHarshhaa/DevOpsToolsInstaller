using System.Security.Cryptography;
using DevOpsToolsInstaller.Models;

namespace DevOpsToolsInstaller.Services;

public sealed class DownloadService
{
    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        AllowAutoRedirect = true
    })
    {
        Timeout = TimeSpan.FromMinutes(30)
    };

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
    /// Skips if the file already exists and SHA256 matches.
    /// </summary>
    public async Task DownloadAsync(
        ToolDefinition tool,
        string destinationFolder,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var destPath = Path.Combine(destinationFolder, tool.FileName);

        // Skip if already downloaded and hash matches
        if (File.Exists(destPath))
        {
            if (string.IsNullOrEmpty(tool.Sha256) || await VerifyHashAsync(destPath, tool.Sha256, ct))
            {
                tool.Progress = 100;
                tool.Status = ToolStatus.Downloaded;
                progress?.Report(100);
                return;
            }

            // Hash mismatch — re-download
            File.Delete(destPath);
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
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            await using var fileStream = File.Create(destPath);

            var buffer = new byte[81920]; // 80 KB chunks
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                totalRead += bytesRead;

                if (totalBytes > 0)
                {
                    var pct = (double)totalRead / totalBytes * 100;
                    tool.Progress = pct;
                    progress?.Report(pct);
                }
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
        catch
        {
            CleanupPartial(destPath);
            tool.Status = ToolStatus.Failed;
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
            await semaphore.WaitAsync(ct);
            try
            {
                var progress = new Progress<double>(pct => tool.Progress = pct);
                await DownloadAsync(tool, destinationFolder, progress, ct);
            }
            catch (OperationCanceledException)
            {
                // Propagated — batch will cancel
            }
            catch (Exception)
            {
                tool.Status = ToolStatus.Failed;
                tool.StatusText = "Download failed";
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
    /// Removes a partially-downloaded file.
    /// </summary>
    private static void CleanupPartial(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }

    /// <summary>
    /// Checks whether a tool's installer has already been downloaded.
    /// </summary>
    public static bool IsAlreadyDownloaded(ToolDefinition tool, string destinationFolder)
    {
        var path = Path.Combine(destinationFolder, tool.FileName);
        return File.Exists(path);
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
            catch { /* skip locked files */ }
        }
        return freed;
    }
}
