using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using FS25ModManager.Models;

namespace FS25ModManager.Services;

/// <summary>Reads mods (zip files or unpacked folders) and their modDesc.xml.</summary>
public static class ModScanner
{
    private const string ModDescFile = "modDesc.xml";
    private const string PreferredLanguage = "en";

    public static List<ModItem> ScanDirectory(string directory, bool isActive)
    {
        var result = new ConcurrentBag<ModItem>();
        if (!Directory.Exists(directory))
            return [];

        var candidates = new List<string>();
        candidates.AddRange(Directory.EnumerateFiles(directory, "*.zip"));
        candidates.AddRange(Directory.EnumerateDirectories(directory)
            .Where(d => File.Exists(Path.Combine(d, ModDescFile))));

        Parallel.ForEach(candidates, new ParallelOptions { MaxDegreeOfParallelism = 4 }, path =>
        {
            result.Add(ReadMod(path, isActive));
        });

        return result.ToList();
    }

    public static ModItem ReadMod(string path, bool isActive)
    {
        bool isFolder = Directory.Exists(path);
        var mod = new ModItem
        {
            FileName = Path.GetFileName(path),
            IsFolder = isFolder,
            FullPath = path,
            IsActive = isActive,
        };
        mod.Title = mod.ModName;

        try
        {
            if (isFolder)
            {
                var dirInfo = new DirectoryInfo(path);
                mod.LastModified = dirInfo.LastWriteTime;
                mod.SizeBytes = dirInfo.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
                var doc = XDocument.Load(Path.Combine(path, ModDescFile));
                ApplyModDesc(mod, doc, relative => ReadFolderFile(path, relative));
            }
            else
            {
                var fileInfo = new FileInfo(path);
                mod.LastModified = fileInfo.LastWriteTime;
                mod.SizeBytes = fileInfo.Length;
                using var zip = ZipFile.OpenRead(path);
                var entry = FindEntry(zip, ModDescFile)
                    ?? throw new InvalidDataException("modDesc.xml not found in archive.");
                XDocument doc;
                using (var stream = entry.Open())
                    doc = XDocument.Load(stream);
                ApplyModDesc(mod, doc, relative => ReadZipFile(zip, relative));
            }
        }
        catch (Exception ex)
        {
            mod.Error = ex.Message;
        }

        return mod;
    }

    private static void ApplyModDesc(ModItem mod, XDocument doc, Func<string, XDocument?> loadXml)
    {
        var root = doc.Root ?? throw new InvalidDataException("modDesc.xml is empty.");
        var translations = new Lazy<Dictionary<string, string>>(() => LoadTranslations(root, loadXml));

        string Resolve(string text)
        {
            text = text.Trim();
            if (text.StartsWith("$l10n_", StringComparison.Ordinal)
                && translations.Value.TryGetValue(text[6..], out var translated))
                return translated;
            return text;
        }

        var title = Resolve(Localized(root.Element("title")));
        if (title.Length > 0) mod.Title = title;
        mod.Description = Resolve(Localized(root.Element("description")));
        mod.Author = root.Element("author")?.Value.Trim() ?? string.Empty;
        mod.Version = root.Element("version")?.Value.Trim() ?? string.Empty;

        var mp = root.Element("multiplayer")?.Attribute("supported")?.Value;
        if (mp is not null)
            mod.MultiplayerSupported = string.Equals(mp, "true", StringComparison.OrdinalIgnoreCase);

        mod.Dependencies = root.Element("dependencies")?.Elements("dependency")
            .Select(d => d.Value.Trim())
            .Where(d => d.Length > 0)
            .ToList() ?? [];

        mod.HasScripts = root.Element("extraSourceFiles")?.HasElements == true;
        mod.IsMap = root.Element("maps")?.HasElements == true;
        mod.HasStoreItems = root.Element("storeItems")?.HasElements == true;

        var icon = root.Element("iconFilename")?.Value.Trim();
        if (!string.IsNullOrEmpty(icon))
            mod.IconEntry = icon.Replace('\\', '/');
    }

    /// <summary>Picks the English text of a localized element, or the first language, or plain text.</summary>
    private static string Localized(XElement? element)
    {
        if (element is null) return string.Empty;
        if (!element.HasElements) return element.Value;
        return (element.Element(PreferredLanguage) ?? element.Elements().First()).Value;
    }

    private static Dictionary<string, string> LoadTranslations(XElement root, Func<string, XDocument?> loadXml)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var l10n = root.Element("l10n");
        if (l10n is null) return map;

        // Inline: <l10n><text name="key"><en>...</en></text></l10n>
        foreach (var text in l10n.Elements("text"))
        {
            if ((string?)text.Attribute("name") is { } name)
                map.TryAdd(name, Localized(text).Trim());
        }

        // External: <l10n filenamePrefix="translations/translation"/> -> translation_en.xml
        if ((string?)l10n.Attribute("filenamePrefix") is { Length: > 0 } prefix)
        {
            var file = loadXml($"{prefix}_{PreferredLanguage}.xml");
            if (file?.Root is { } l10nRoot)
            {
                foreach (var text in l10nRoot.Descendants("text"))
                {
                    if ((string?)text.Attribute("name") is { } name && (string?)text.Attribute("text") is { } value)
                        map.TryAdd(name, value);
                }
                // Older format: <elements><e k="key" v="value"/></elements>
                foreach (var e in l10nRoot.Descendants("e"))
                {
                    if ((string?)e.Attribute("k") is { } key && (string?)e.Attribute("v") is { } value)
                        map.TryAdd(key, value);
                }
            }
        }

        return map;
    }

    public static ZipArchiveEntry? FindEntry(ZipArchive zip, string relativePath)
    {
        relativePath = relativePath.Replace('\\', '/').TrimStart('/');
        return zip.GetEntry(relativePath)
            ?? zip.Entries.FirstOrDefault(e =>
                string.Equals(e.FullName.Replace('\\', '/'), relativePath, StringComparison.OrdinalIgnoreCase));
    }

    private static XDocument? ReadZipFile(ZipArchive zip, string relativePath)
    {
        var entry = FindEntry(zip, relativePath);
        if (entry is null) return null;
        try
        {
            using var stream = entry.Open();
            return XDocument.Load(stream);
        }
        catch
        {
            return null;
        }
    }

    private static XDocument? ReadFolderFile(string folder, string relativePath)
    {
        var file = Path.Combine(folder, relativePath.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            return File.Exists(file) ? XDocument.Load(file) : null;
        }
        catch
        {
            return null;
        }
    }
}
