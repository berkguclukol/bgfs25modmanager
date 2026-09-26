using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FS25ModManager.Models;

namespace FS25ModManager.Services;

/// <summary>Reads savegame folders (savegame1 … savegame20) in the FS25 profile directory.</summary>
public static partial class SavegameScanner
{
    /// <summary>Files that store owned/placed objects. Objects from mods reference "$moddir$ModName/…" or modName="…".</summary>
    private static readonly string[] ObjectFiles = ["vehicles.xml", "placeables.xml", "items.xml", "handTools.xml"];

    [GeneratedRegex(@"\$moddir\$([^/\\""]+)|modName=""([^""]+)""")]
    private static partial Regex ModReferenceRegex();

    [GeneratedRegex(@"^savegame(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex SlotFolderRegex();

    public static List<Savegame> ScanAll(string profileDirectory)
    {
        if (!Directory.Exists(profileDirectory))
            return [];

        var result = new List<Savegame>();
        foreach (var dir in Directory.EnumerateDirectories(profileDirectory))
        {
            var match = SlotFolderRegex().Match(Path.GetFileName(dir));
            if (!match.Success || !File.Exists(Path.Combine(dir, "careerSavegame.xml")))
                continue;
            result.Add(Read(dir, int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)));
        }
        return result.OrderBy(s => s.Slot).ToList();
    }

    private static Savegame Read(string dir, int slot)
    {
        try
        {
            var root = XDocument.Load(Path.Combine(dir, "careerSavegame.xml")).Root!;
            var settings = root.Element("settings");
            var statistics = root.Element("statistics");

            var mapId = settings?.Element("mapId")?.Value.Trim() ?? string.Empty;
            string? mapMod = null;
            var dot = mapId.IndexOf('.');
            if (dot > 0 && !IsDlc(mapId)) mapMod = mapId[..dot];

            var selected = root.Elements("mod")
                .Select(m => new SaveModRef(
                    (string?)m.Attribute("modName") ?? string.Empty,
                    (string?)m.Attribute("title") ?? string.Empty,
                    (string?)m.Attribute("version") ?? string.Empty))
                .Where(m => m.ModName.Length > 0 && !IsDlc(m.ModName))
                .ToList();

            var withObjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in ObjectFiles)
            {
                var path = Path.Combine(dir, file);
                if (!File.Exists(path)) continue;
                foreach (Match m in ModReferenceRegex().Matches(File.ReadAllText(path)))
                {
                    var name = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                    if (!IsDlc(name)) withObjects.Add(name);
                }
            }

            return new Savegame
            {
                Slot = slot,
                FolderPath = dir,
                Name = settings?.Element("savegameName")?.Value.Trim() ?? string.Empty,
                MapTitle = settings?.Element("mapTitle")?.Value.Trim() ?? string.Empty,
                MapModName = mapMod,
                CreationDate = ParseDate(settings?.Element("creationDate")?.Value),
                SaveDate = ParseDate(settings?.Element("saveDate")?.Value),
                Money = long.TryParse(statistics?.Element("money")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var money) ? money : null,
                PlayTimeMinutes = double.TryParse(statistics?.Element("playTime")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes) ? minutes : 0,
                SelectedMods = selected,
                ModsWithObjects = withObjects,
            };
        }
        catch (Exception ex)
        {
            return new Savegame { Slot = slot, FolderPath = dir, Error = ex.Message };
        }
    }

    private static bool IsDlc(string name) => name.StartsWith("pdlc_", StringComparison.OrdinalIgnoreCase);

    private static DateTime? ParseDate(string? value) =>
        DateTime.TryParseExact(value?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
}
