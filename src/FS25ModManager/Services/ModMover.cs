using System.IO;
using System.Runtime.InteropServices;

namespace FS25ModManager.Services;

public sealed class DestinationExistsException(string path)
    : IOException($"'{Path.GetFileName(path)}' already exists in the destination folder.")
{
    public string DestinationPath { get; } = path;
}

/// <summary>File operations on mods: moving between folders, copying in new mods, renaming.</summary>
public static class ModMover
{
    /// <summary>
    /// Moves <paramref name="sourcePath"/> into <paramref name="destinationDirectory"/> and returns the new path.
    /// When <paramref name="replaceExisting"/> is true, an existing item with the same name is sent to the Recycle Bin first.
    /// </summary>
    public static string Move(string sourcePath, string destinationDirectory, bool replaceExisting,
        string? targetName = null)
    {
        var destination = PrepareDestination(sourcePath, destinationDirectory, replaceExisting, targetName);

        if (Directory.Exists(sourcePath))
            MoveDirectory(sourcePath, destination);
        else
            File.Move(sourcePath, destination); // Works across volumes (copy + delete).

        return destination;
    }

    /// <summary>Copies a mod file or folder into <paramref name="destinationDirectory"/> and returns the new path.</summary>
    public static string Copy(string sourcePath, string destinationDirectory, bool replaceExisting,
        string? targetName = null)
    {
        var destination = PrepareDestination(sourcePath, destinationDirectory, replaceExisting, targetName);

        if (Directory.Exists(sourcePath))
            CopyDirectory(sourcePath, destination);
        else
            File.Copy(sourcePath, destination);

        return destination;
    }

    /// <summary>Renames a mod in place and returns the new path.</summary>
    public static string Rename(string path, string newName)
    {
        var directory = Path.GetDirectoryName(path)!;
        return Move(path, directory, replaceExisting: false, targetName: newName);
    }

    private static string PrepareDestination(string sourcePath, string destinationDirectory, bool replaceExisting,
        string? targetName)
    {
        Directory.CreateDirectory(destinationDirectory);
        var destination = Path.Combine(destinationDirectory, targetName ?? Path.GetFileName(sourcePath));

        if (File.Exists(destination) || Directory.Exists(destination))
        {
            if (!replaceExisting)
                throw new DestinationExistsException(destination);
            SendToRecycleBin(destination);
        }

        return destination;
    }

    private static void MoveDirectory(string source, string destination)
    {
        if (string.Equals(Path.GetPathRoot(source), Path.GetPathRoot(destination), StringComparison.OrdinalIgnoreCase))
        {
            Directory.Move(source, destination);
            return;
        }

        // Directory.Move cannot cross volumes: copy everything, then delete the source.
        CopyDirectory(source, destination);
        Directory.Delete(source, recursive: true);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var dir in Directory.EnumerateDirectories(source))
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }

    public static void SendToRecycleBin(string path)
    {
        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = path + "\0\0",
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT,
        };
        int result = SHFileOperation(ref op);
        if (result != 0 || op.fAnyOperationsAborted)
            throw new IOException($"Could not move '{Path.GetFileName(path)}' to the Recycle Bin (error {result}).");
    }

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);
}
