using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace FS25ModManager.Services;

/// <summary>A mod ready to be copied into the mods folder.</summary>
/// <param name="SourcePath">Zip file or folder to install.</param>
/// <param name="TargetName">File/folder name to install as (already a valid mod name).</param>
/// <param name="IsTemporary">Source was extracted to a temp location and can be moved instead of copied.</param>
/// <param name="OriginalName">Name before sanitizing, when it had to be changed.</param>
public sealed record InstallCandidate(string SourcePath, string TargetName, bool IsTemporary, string? OriginalName);

/// <summary>Turns dropped files into installable mods: validates, unpacks bundles and fixes names.</summary>
public static partial class ModInstaller
{
    private const string ModDescFile = "modDesc.xml";

    /// <summary>The game only loads mods whose name uses A-Z, a-z, 0-9 and _ and does not start with a digit.</summary>
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex ValidNameRegex();

    /// <summary>Suffixes added by browsers/Explorer for duplicate files: " (1)", " - Copy", " - Kopya (2)".</summary>
    [GeneratedRegex(@"(\s*\(\d+\)|\s+-\s+(Copy|Kopya|Kopie|Copie)(\s*\(\d+\))?)$", RegexOptions.IgnoreCase)]
    private static partial Regex DuplicateSuffixRegex();

    [GeneratedRegex("[^A-Za-z0-9_]+")]
    private static partial Regex InvalidCharsRegex();

    public static bool IsValidModName(string modName) => ValidNameRegex().IsMatch(modName);

    public static string SanitizeModName(string modName)
    {
        var name = DuplicateSuffixRegex().Replace(modName.Trim(), "");
        name = InvalidCharsRegex().Replace(name, "_").Trim('_');
        if (name.Length == 0) name = "Mod";
        if (char.IsDigit(name[0])) name = "_" + name;
        return name;
    }

    /// <summary>File name a mod should have, e.g. "FS25_Mod (1).zip" → "FS25_Mod.zip".</summary>
    public static string ValidFileName(string fileName, bool isFolder)
    {
        if (isFolder) return SanitizeModName(fileName);
        return SanitizeModName(Path.GetFileNameWithoutExtension(fileName)) + ".zip";
    }

    public static List<InstallCandidate> Prepare(IEnumerable<string> paths, string tempDirectory, List<string> rejected)
    {
        var result = new List<InstallCandidate>();

        foreach (var path in paths)
        {
            var name = Path.GetFileName(path);
            try
            {
                if (Directory.Exists(path))
                {
                    if (File.Exists(Path.Combine(path, ModDescFile)))
                        result.Add(Candidate(path, isFolder: true, isTemporary: false));
                    else
                        rejected.Add($"{name}: folder has no modDesc.xml");
                }
                else if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    PrepareZip(path, tempDirectory, result, rejected);
                }
                else
                {
                    rejected.Add($"{name}: not a .zip file or mod folder");
                }
            }
            catch (Exception ex)
            {
                rejected.Add($"{name}: {ex.Message}");
            }
        }

        return result;
    }

    private static void PrepareZip(string path, string tempDirectory, List<InstallCandidate> result, List<string> rejected)
    {
        var name = Path.GetFileName(path);
        using var zip = ZipFile.OpenRead(path);

        // A normal mod: modDesc.xml at the root.
        if (ModScanner.FindEntry(zip, ModDescFile) is not null)
        {
            result.Add(Candidate(path, isFolder: false, isTemporary: false));
            return;
        }

        // A bundle of mods: zip files inside the zip.
        var innerZips = zip.Entries
            .Where(e => e.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && e.Length > 0)
            .ToList();
        if (innerZips.Count > 0)
        {
            int found = 0;
            foreach (var entry in innerZips)
            {
                var extracted = Path.Combine(NewTempFolder(tempDirectory), entry.Name);
                entry.ExtractToFile(extracted);
                if (IsZipMod(extracted))
                {
                    result.Add(Candidate(extracted, isFolder: false, isTemporary: true));
                    found++;
                }
            }
            if (found == 0) rejected.Add($"{name}: contains no Farming Simulator mods");
            return;
        }

        // A mod wrapped in an extra folder: "FS25_Mod/modDesc.xml" inside the zip. Repack it correctly.
        var nested = zip.Entries.FirstOrDefault(e =>
            e.Name.Equals(ModDescFile, StringComparison.OrdinalIgnoreCase)
            && e.FullName.Replace('\\', '/').Count(c => c == '/') == 1);
        if (nested is not null)
        {
            var prefix = nested.FullName[..^nested.Name.Length];
            var folderName = prefix.TrimEnd('/', '\\');
            var repacked = Path.Combine(NewTempFolder(tempDirectory), folderName + ".zip");
            using (var output = ZipFile.Open(repacked, ZipArchiveMode.Create))
            {
                foreach (var entry in zip.Entries)
                {
                    if (!entry.FullName.StartsWith(prefix, StringComparison.Ordinal) || entry.FullName.Length == prefix.Length)
                        continue;
                    var newEntry = output.CreateEntry(entry.FullName[prefix.Length..], CompressionLevel.Optimal);
                    if (entry.FullName.EndsWith('/')) continue; // Directory entry
                    using var from = entry.Open();
                    using var to = newEntry.Open();
                    from.CopyTo(to);
                }
            }
            result.Add(Candidate(repacked, isFolder: false, isTemporary: true));
            return;
        }

        rejected.Add($"{name}: no modDesc.xml found — not a Farming Simulator mod");
    }

    private static bool IsZipMod(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            return ModScanner.FindEntry(zip, ModDescFile) is not null;
        }
        catch
        {
            return false;
        }
    }

    private static InstallCandidate Candidate(string path, bool isFolder, bool isTemporary)
    {
        var fileName = Path.GetFileName(path);
        var valid = ValidFileName(fileName, isFolder);
        return new InstallCandidate(path, valid, isTemporary, valid == fileName ? null : fileName);
    }

    private static string NewTempFolder(string tempDirectory)
    {
        var dir = Path.Combine(tempDirectory, Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
