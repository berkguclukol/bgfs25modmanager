using System.Reflection;

namespace FS25ModManager;

/// <summary>Application and developer details shown on the About page.</summary>
public static class AppInfo
{
    public const string Name = "BG FS25 Mod Manager";

    public const string Tagline = "A simple, fast mod manager for Farming Simulator 25.";

    // ── Developer ──────────────────────────────────────────────
    // Leave a value empty ("") to hide it on the About page.
    public const string DeveloperName = "Berk Güçlükol";
    public const string DeveloperBio = "Farming Simulator player and developer of BG FS25 Mod Manager.";
    public const string WebsiteUrl = "https://guclukol.net";
    public const string GitHubUrl = "";
    public const string Email = "";

    public static string Version
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version is null ? "1.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    public const string Copyright = $"© 2026 {DeveloperName}";
}
