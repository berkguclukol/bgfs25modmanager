using System.IO;
using System.IO.Compression;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FS25ModManager.Models;
using Pfim;

namespace FS25ModManager.Services;

/// <summary>Loads mod icons (usually DDS) into frozen WPF bitmaps.</summary>
public static class IconLoader
{
    private const int MaxIconSize = 128;

    public static BitmapSource? Load(ModItem mod)
    {
        if (mod.IconEntry is null) return null;

        try
        {
            var bytes = ReadIconBytes(mod);
            return bytes is null ? null : Decode(bytes);
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? ReadIconBytes(ModItem mod)
    {
        // The game swaps .png references for their .dds counterpart, so try both.
        var candidates = new List<string> { mod.IconEntry! };
        var ext = Path.GetExtension(mod.IconEntry!);
        if (!ext.Equals(".dds", StringComparison.OrdinalIgnoreCase))
            candidates.Insert(0, Path.ChangeExtension(mod.IconEntry!, ".dds"));

        if (mod.IsFolder)
        {
            foreach (var candidate in candidates)
            {
                var file = Path.Combine(mod.FullPath, candidate.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(file)) return File.ReadAllBytes(file);
            }
            return null;
        }

        using var zip = ZipFile.OpenRead(mod.FullPath);
        foreach (var candidate in candidates)
        {
            var entry = ModScanner.FindEntry(zip, candidate);
            if (entry is null) continue;
            using var stream = entry.Open();
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }
        return null;
    }

    private static BitmapSource? Decode(byte[] bytes)
    {
        BitmapSource source;
        if (bytes.Length > 4 && bytes[0] == 'D' && bytes[1] == 'D' && bytes[2] == 'S' && bytes[3] == ' ')
        {
            using var image = Pfimage.FromStream(new MemoryStream(bytes));
            PixelFormat? format = image.Format switch
            {
                ImageFormat.Rgba32 => PixelFormats.Bgra32,
                ImageFormat.Rgb24 => PixelFormats.Bgr24,
                ImageFormat.Rgb8 => PixelFormats.Gray8,
                ImageFormat.R5g6b5 => PixelFormats.Bgr565,
                ImageFormat.R5g5b5 or ImageFormat.R5g5b5a1 => PixelFormats.Bgr555,
                _ => null,
            };
            if (format is null) return null;
            source = BitmapSource.Create(image.Width, image.Height, 96, 96, format.Value, null,
                image.Data, image.Stride);
        }
        else
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = new MemoryStream(bytes);
            bitmap.EndInit();
            source = bitmap;
        }

        if (source.PixelWidth > MaxIconSize)
        {
            double scale = (double)MaxIconSize / source.PixelWidth;
            source = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        }

        // Copy into a compact bitmap so the large decoded buffer can be collected.
        var result = new WriteableBitmap(source);
        result.Freeze();
        return result;
    }
}
