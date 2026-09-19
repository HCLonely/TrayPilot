using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace TrayPilot;

internal static class TrayImages
{
    internal static Bitmap Create(TrayEntry entry, int size, bool allowFileAccess = true)
    {
        using var source = Load(entry, allowFileAccess);
        var result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(result);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.CompositingMode = CompositingMode.SourceCopy;
        using var attributes = new ImageAttributes();
        // Mirror edge pixels so the resampling filter does not fade image borders.
        attributes.SetWrapMode(WrapMode.TileFlipXY);
        attributes.SetColorMatrix(new ColorMatrix { Matrix33 = entry.State == 1 ? 0.35f : 1f });
        float scale = Math.Min((float)size / source.Width, (float)size / source.Height);
        int width = Math.Max(1, (int)(source.Width * scale)), height = Math.Max(1, (int)(source.Height * scale));
        graphics.DrawImage(source, new Rectangle((size - width) / 2, (size - height) / 2, width, height),
            0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        return result;
    }

    static Bitmap Load(TrayEntry entry, bool allowFileAccess)
    {
        if (entry.IconSnapshot is { Length: > 0 })
        {
            try
            {
                using var stream = new MemoryStream(entry.IconSnapshot);
                using var image = Image.FromStream(stream);
                return new Bitmap(image);
            }
            catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException or System.Runtime.InteropServices.ExternalException) { }
        }
        if (!allowFileAccess) return SystemIcons.Application.ToBitmap();
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(entry.Path);
            if (icon != null) return icon.ToBitmap();
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { }
        return SystemIcons.Application.ToBitmap();
    }
}
