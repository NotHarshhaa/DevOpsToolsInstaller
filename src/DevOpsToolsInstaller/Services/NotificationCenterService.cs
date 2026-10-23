using System;
using System.Collections.Generic;
using System.Linq;

namespace DevOpsToolsInstaller.Services;

/// <summary>Visual severity of an in-app notification.</summary>
public enum NoteKind
{
    Info,
    Success,
    Update,
    Warning,
    Error,
}

/// <summary>
/// One entry in the in-app notification center. Immutable snapshot of an
/// event (install finished, update found, download failed…) with display
/// helpers for the flyout list.
/// </summary>
public sealed class NotificationItem
{
    public DateTime At { get; init; } = DateTime.Now;
    public NoteKind Kind { get; init; } = NoteKind.Info;
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? ToolId { get; init; }
    public bool IsRead { get; internal set; }

    public string Glyph => Kind switch
    {
        NoteKind.Success => "\uE73E",   // CheckMark
        NoteKind.Update => "\uE895",    // Download
        NoteKind.Warning => "\uEA82",   // Warning
        NoteKind.Error => "\uEA39",     // Error
        _ => "\uE946",                  // Info
    };

    public string KindBrushKey => Kind switch
    {
        NoteKind.Success => "SystemFillColorSuccessBrush",
        NoteKind.Update => "AccentTextFillColorPrimaryBrush",
        NoteKind.Warning => "SystemFillColorCautionBrush",
        NoteKind.Error => "SystemFillColorCriticalBrush",
        _ => "TextFillColorSecondaryBrush",
    };

    public string TimeLabel
    {
        get
        {
            var delta = DateTime.Now - At;
            if (delta.TotalMinutes < 1) return "just now";
            if (delta.TotalHours < 1) return $"{(int)delta.TotalMinutes}m ago";
            if (delta.TotalDays < 1) return $"{(int)delta.TotalHours}h ago";
            if (delta.TotalDays < 7) return At.ToString("ddd, HH:mm");
            return At.ToString("MMM d");
        }
    }
}

/// <summary>
/// In-app notification center: a bounded, thread-safe feed of notable events
/// (install finished, update found, catalog refreshed, download failed…)
/// surfaced through the title-bar bell. Complements OS toasts (which only
/// surface while the window may be closed) with a persistent event history
/// inside the app. Raised events always marshal onto the UI thread.
/// </summary>
public static class NotificationCenterService
{
    private const int MaxItems = 100;

    private static readonly object Gate = new();
    private static readonly List<(DateTime Added, NotificationItem Item)> Store = new();

    /// <summary>Raised on the UI thread whenever the feed changes.</summary>
    public static event Action? Changed;

    /// <summary>Snapshot of the newest events first.</summary>
    public static IReadOnlyList<NotificationItem> Snapshot
    {
        get
        {
            lock (Gate)
            {
                return Store.Select(e => e.Item).ToList();
            }
        }
    }

    /// <summary>Number of entries not marked as read, normalized for a badge.</summary>
    public static int UnreadCount
    {
        get
        {
            lock (Gate)
            {
                return Store.Count(e => !e.Item.IsRead);
            }
        }
    }

    /// <summary>
    /// Adds an event to the center. Safe from any thread; never throws.
    /// </summary>
    public static void Post(NoteKind kind, string title, string message, string? toolId = null)
    {
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(message)) return;

        try
        {
            lock (Gate)
            {
                Store.Insert(0, (DateTime.Now, new NotificationItem
                {
                    Kind = kind,
                    Title = title,
                    Message = message,
                    ToolId = toolId,
                }));

                for (int i = Store.Count - 1; i >= MaxItems; i--)
                {
                    Store.RemoveAt(i);
                }
            }
        }
        catch
        {
            // The notification center is a convenience — never break callers.
            return;
        }

        Services.UiDispatcher.Run(() => Changed?.Invoke());
    }

    /// <summary>Marks every entry as read.</summary>
    public static void MarkAllRead()
    {
        lock (Gate)
        {
            foreach (var entry in Store)
            {
                entry.Item.IsRead = true;
            }
        }

        Services.UiDispatcher.Run(() => Changed?.Invoke());
    }

    /// <summary>Drops every entry.</summary>
    public static void Clear()
    {
        lock (Gate)
        {
            Store.Clear();
        }

        Services.UiDispatcher.Run(() => Changed?.Invoke());
    }
}
