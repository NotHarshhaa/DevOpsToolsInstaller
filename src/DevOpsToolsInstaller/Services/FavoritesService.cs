using System.Text.Json;

namespace DevOpsToolsInstaller.Services;

/// <summary>
/// Persists the user's favorite (starred) tool IDs to a local JSON file
/// so they survive across sessions.
/// </summary>
public static class FavoritesService
{
    private static readonly string FavoritesFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DevOpsToolsInstaller", "favorites.json");

    private static HashSet<string> _favorites = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Loads favorite tool IDs from disk.
    /// </summary>
    public static void Load()
    {
        try
        {
            if (File.Exists(FavoritesFilePath))
            {
                var json = File.ReadAllText(FavoritesFilePath);
                var ids = JsonSerializer.Deserialize<List<string>>(json);
                if (ids is not null)
                    _favorites = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // Fallback to empty
        }
    }

    /// <summary>
    /// Saves current favorites to disk.
    /// </summary>
    public static void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(FavoritesFilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_favorites.ToList(),
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FavoritesFilePath, json);
        }
        catch
        {
            // Ignore write errors
        }
    }

    /// <summary>
    /// Returns true if the given tool ID is a favorite.
    /// </summary>
    public static bool IsFavorite(string toolId)
        => _favorites.Contains(toolId);

    /// <summary>
    /// Toggles a tool's favorite status and persists immediately.
    /// Returns the new state.
    /// </summary>
    public static bool Toggle(string toolId)
    {
        if (_favorites.Contains(toolId))
        {
            _favorites.Remove(toolId);
            Save();
            return false;
        }
        else
        {
            _favorites.Add(toolId);
            Save();
            return true;
        }
    }

    /// <summary>
    /// All currently favorited tool IDs.
    /// </summary>
    public static IReadOnlySet<string> All => _favorites;
}
