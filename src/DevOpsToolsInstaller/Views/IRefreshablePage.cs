namespace DevOpsToolsInstaller.Views;

/// <summary>
/// Implemented by pages that can refresh their content on demand. The main
/// window calls <see cref="RefreshView"/> when the user presses <kbd>F5</kbd>
/// (or uses the command palette "Refresh page" action).
/// </summary>
public interface IRefreshablePage
{
    /// <summary>Re-loads the page's data and updates its UI.</summary>
    void RefreshView();
}
