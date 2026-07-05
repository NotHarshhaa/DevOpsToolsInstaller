using Microsoft.UI.Dispatching;

namespace DevOpsToolsInstaller.Services;

/// <summary>
/// Marshals actions onto the UI thread. Background work (e.g. download progress)
/// mutates bound model properties; WinUI requires those PropertyChanged
/// notifications to be raised on the UI thread or it throws RPC_E_WRONG_THREAD.
/// </summary>
public static class UiDispatcher
{
    /// <summary>
    /// The main window's dispatcher queue. Set once during App.OnLaunched.
    /// </summary>
    public static DispatcherQueue? Queue { get; set; }

    /// <summary>
    /// Runs <paramref name="action"/> on the UI thread. If already on the UI
    /// thread (or no dispatcher is available yet, e.g. during startup), it runs
    /// synchronously.
    /// </summary>
    public static void Run(Action action)
    {
        var queue = Queue;
        if (queue is null || queue.HasThreadAccess)
        {
            action();
        }
        else
        {
            queue.TryEnqueue(() => action());
        }
    }
}
