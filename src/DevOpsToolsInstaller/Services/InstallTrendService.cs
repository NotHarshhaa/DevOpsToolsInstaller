using System;
using System.Collections.Generic;
using System.Linq;

namespace DevOpsToolsInstaller.Services;

/// <summary>Install/update counts recorded on a single day.</summary>
public sealed class DailyInstallActivity
{
    public DateTime Date { get; init; }
    public int Installs { get; init; }
    public int Updates { get; init; }

    public int Total => Installs + Updates;

    /// <summary>Compact axis label, e.g. "Oct 4".</summary>
    public string ShortLabel => Date.ToString("MMM d");
}

/// <summary>
/// Lightweight local activity history for the Home trend sparklines: installs
/// and updates aggregated per day.
///
/// The aggregates are read from the append-only install history that every
/// install, update and rollback already appends to
/// (%LOCALAPPDATA%\DevOpsToolsInstaller\history.jsonl) — a local file that
/// needs no network access — so the Home trend and the History page can never
/// disagree and there is no second copy of the data to keep in sync.
/// </summary>
public static class InstallTrendService
{
    /// <summary>Number of days covered by the Home trend sparklines.</summary>
    public const int DefaultDays = 30;

    /// <summary>
    /// Returns one entry per day for the trailing <paramref name="days"/> window
    /// (oldest first, gaps zero-filled) so the sparklines always have a point
    /// for every day.
    /// </summary>
    public static IReadOnlyList<DailyInstallActivity> GetDailyActivity(int days = DefaultDays)
    {
        if (days < 1) days = 1;

        var today = DateTime.Today;
        var firstDay = today.AddDays(-(days - 1));

        var installs = new Dictionary<DateTime, int>();
        var updates = new Dictionary<DateTime, int>();

        foreach (var evt in InstallHistoryService.Load(int.MaxValue))
        {
            var day = evt.Timestamp.Date;
            if (day < firstDay || day > today) continue;

            if (string.Equals(evt.Action, nameof(HistoryAction.Update), StringComparison.OrdinalIgnoreCase))
            {
                updates[day] = updates.TryGetValue(day, out var u) ? u + 1 : 1;
            }
            else if (string.Equals(evt.Action, nameof(HistoryAction.Install), StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(evt.Action, nameof(HistoryAction.Rollback), StringComparison.OrdinalIgnoreCase))
            {
                // A rollback redeploys a version, so it counts as an install.
                installs[day] = installs.TryGetValue(day, out var i) ? i + 1 : 1;
            }
        }

        var result = new List<DailyInstallActivity>(days);
        for (int offset = 0; offset < days; offset++)
        {
            var day = firstDay.AddDays(offset);
            installs.TryGetValue(day, out var installCount);
            updates.TryGetValue(day, out var updateCount);
            result.Add(new DailyInstallActivity
            {
                Date = day,
                Installs = installCount,
                Updates = updateCount
            });
        }

        return result;
    }

    /// <summary>True when anything was installed or updated inside the window.</summary>
    public static bool HasActivity(IReadOnlyList<DailyInstallActivity> activity)
        => activity.Any(day => day.Total > 0);
}
