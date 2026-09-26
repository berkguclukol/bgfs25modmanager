using System.Diagnostics;
using System.IO;
using System.Xml.Linq;

namespace FS25ModManager.Services;

public static class GamePaths
{
    public const string BackupFolderName = "fs25modbackup";

    private static readonly string[] GameProcessNames =
        ["FarmingSimulator2025Game", "FarmingSimulator2025", "FarmingSimulator2025_x64"];

    /// <summary>Documents\My Games\FarmingSimulator2025 (follows OneDrive redirection).</summary>
    public static string UserProfileDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "My Games", "FarmingSimulator2025");

    public static string BackupDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), BackupFolderName);

    /// <summary>
    /// The folder the game loads mods from. Honors the modsDirectoryOverride
    /// setting in gameSettings.xml when it is active.
    /// </summary>
    public static string GetModsDirectory()
    {
        var fallback = Path.Combine(UserProfileDirectory, "mods");
        var settingsFile = Path.Combine(UserProfileDirectory, "gameSettings.xml");
        if (!File.Exists(settingsFile))
            return fallback;

        try
        {
            var node = XDocument.Load(settingsFile).Root?.Element("modsDirectoryOverride");
            if (node is not null
                && string.Equals((string?)node.Attribute("active"), "true", StringComparison.OrdinalIgnoreCase)
                && (string?)node.Attribute("directory") is { Length: > 0 } dir)
            {
                return Path.GetFullPath(dir);
            }
        }
        catch (Exception ex) when (ex is IOException or System.Xml.XmlException or UnauthorizedAccessException)
        {
            // Unreadable settings file; fall back to the default location.
        }

        return fallback;
    }

    public static bool IsGameRunning()
    {
        foreach (var name in GameProcessNames)
        {
            var processes = Process.GetProcessesByName(name);
            var running = processes.Length > 0;
            foreach (var p in processes) p.Dispose();
            if (running) return true;
        }
        return false;
    }
}
