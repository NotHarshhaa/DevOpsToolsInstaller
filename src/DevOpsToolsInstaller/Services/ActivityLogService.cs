using System.Collections.ObjectModel;

namespace DevOpsToolsInstaller.Services;

/// <summary>
/// Severity / type of a log entry.
/// </summary>
public enum LogSeverity
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>
/// A single timestamped event in the activity log.
/// </summary>
public sealed class LogEntry
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public LogSeverity Severity { get; init; } = LogSeverity.Info;
    public string Message { get; init; } = string.Empty;
    public string ToolName { get; init; } = string.Empty;

    /// <summary>Display-friendly timestamp.</summary>
    public string TimeLabel => Timestamp.ToString("HH:mm:ss");

    /// <summary>Icon glyph per severity.</summary>
    public string SeverityGlyph => Severity switch
    {
        LogSeverity.Success => "\uE73E",  // checkmark
        LogSeverity.Warning => "\uE7BA",  // warning
        LogSeverity.Error   => "\uE783",  // error
        _                   => "\uE946"   // info
    };
}

/// <summary>
/// Thread-safe activity log that the download pipeline and other services
/// can publish events into. The <see cref="Entries"/> collection is observable
/// and automatically marshals to the UI thread. Every entry is additionally
/// appended to a daily audit file on disk for post-incident review.
/// </summary>
public static class ActivityLogService
{
    /// <summary>
    /// Maximum number of entries retained (oldest are trimmed).
    /// </summary>
    private const int MaxEntries = 200;

    /// <summary>
    /// The observable collection bound by the Downloads page.
    /// </summary>
    public static ObservableCollection<LogEntry> Entries { get; } = new();

    /// <summary>
    /// Folder holding the on-disk audit logs:
    /// %LOCALAPPDATA%\DevOpsToolsInstaller\logs
    /// </summary>
    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DevOpsToolsInstaller", "logs");

    /// <summary>
    /// Adds a log entry. Thread-safe — marshals to UI thread. Also appended
    /// to the daily audit file (best-effort, never throws).
    /// </summary>
    public static void Log(LogSeverity severity, string toolName, string message)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Severity = severity,
            ToolName = toolName,
            Message = message
        };

        UiDispatcher.Run(() =>
        {
            Entries.Insert(0, entry);

            // Trim old entries
            while (Entries.Count > MaxEntries)
                Entries.RemoveAt(Entries.Count - 1);
        });

        WriteToAuditFile(entry);
    }

    private static void WriteToAuditFile(LogEntry entry)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var filePath = Path.Combine(LogDirectory, $"activity-{entry.Timestamp:yyyyMMdd}.log");
            File.AppendAllText(
                filePath,
                $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss}\t[{entry.Severity}]\t{entry.ToolName}\t{entry.Message}{Environment.NewLine}");
        }
        catch
        {
            // Disk logging is best-effort; the in-memory log still works.
        }
    }

    /// <summary>Shorthand for info-level log.</summary>
    public static void Info(string toolName, string message)
        => Log(LogSeverity.Info, toolName, message);

    /// <summary>Shorthand for success-level log.</summary>
    public static void Success(string toolName, string message)
        => Log(LogSeverity.Success, toolName, message);

    /// <summary>Shorthand for warning-level log.</summary>
    public static void Warn(string toolName, string message)
        => Log(LogSeverity.Warning, toolName, message);

    /// <summary>Shorthand for error-level log.</summary>
    public static void Error(string toolName, string message)
        => Log(LogSeverity.Error, toolName, message);

    /// <summary>Clears all log entries.</summary>
    public static void Clear()
    {
        UiDispatcher.Run(() => Entries.Clear());
    }
}
